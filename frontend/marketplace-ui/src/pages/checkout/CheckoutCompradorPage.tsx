/**
 * LOGIMKT — Checkout del comprador: compra individual V1 y carrito comercial multi-SKU V2.
 * No automatizar reintentos de cobro. No asumir que el cliente conoce el stock total WMS.
 */
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, ClipboardList, CreditCard, Loader2, RefreshCw, Search, ShieldCheck, ShoppingBag, ShoppingCart, Trash2 } from 'lucide-react';
import { ApiError } from '../../api/client';
import { checkoutApi, type CheckoutErrorResponse, type CheckoutMultiSkuItem, type CheckoutSeguimiento } from '../../api/checkout';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import type { Producto } from '../../types';
import { hayOperacionPendiente } from './carritoV2Qa';
import {
  actualizarPendiente, borrarPendiente, esGuid, guardarPendiente, leerOrdenes, leerPendiente,
  nuevaClaveIdempotencia, pendienteResuelto, recordarOrden, type CheckoutPendiente,
} from './checkoutState';
import {
  actualizarPendienteV2, borrarPendienteV2, guardarPendienteV2, leerPendienteV2,
  type CheckoutPendienteV2,
} from './checkoutCartV2';
import './checkout.css';

type CarritoItem = { producto: Producto; cantidad: number };

const envioHabilitado = import.meta.env.VITE_CHECKOUT_HABILITADO === 'true';

const money = (n: number) => new Intl.NumberFormat('es-SV', { style: 'currency', currency: 'USD' }).format(n);
const activo = (p: Producto) => p.estaPublicado && p.estadoProducto.toLowerCase() === 'activo' &&
  p.moneda.toUpperCase() === 'USD' && Number.isFinite(p.precioB2C) && p.precioB2C > 0;
const etiquetaAlerta = (texto: string) => texto.replace(/_/g, ' ').toLowerCase();

export default function CheckoutCompradorPage() {
  const { usuario } = useAuth();
  const cuenta = usuario?.email ?? '';
  const toast = useToast();
  const envioActivo = useRef(false);

  const [entradaBusqueda, setEntradaBusqueda] = useState('');
  const [busqueda, setBusqueda] = useState('');
  const [pagina, setPagina] = useState(1);
  const [productos, setProductos] = useState<Producto[]>([]);
  const [total, setTotal] = useState(0);
  const [cargando, setCargando] = useState(false);
  const [errorCatalogo, setErrorCatalogo] = useState('');
  const [producto, setProducto] = useState<Producto | null>(null);
  const [cantidad, setCantidad] = useState(1);
  const [carrito, setCarrito] = useState<CarritoItem[]>([]);
  const [procesando, setProcesando] = useState(false);
  const [pendiente, setPendiente] = useState<CheckoutPendiente | null>(() => leerPendiente(cuenta));
  const [pendienteV2, setPendienteV2] = useState<CheckoutPendienteV2 | null>(() => leerPendienteV2(cuenta));
  const [, avisarCambioStorage] = useState(0);
  const [ordenes, setOrdenes] = useState<string[]>(() => leerOrdenes(cuenta));
  const [mensaje, setMensaje] = useState('');
  const [resultado, setResultado] = useState<CheckoutSeguimiento | null>(null);
  const [consultando, setConsultando] = useState(false);
  const [ordenConsulta, setOrdenConsulta] = useState('');
  const [errorSeguimiento, setErrorSeguimiento] = useState('');

  // Cambios de sesión: no mostrar historial ni intento pendiente de otro comprador.
  useEffect(() => {
    setPendiente(leerPendiente(cuenta));
    setPendienteV2(leerPendienteV2(cuenta));
    setOrdenes(leerOrdenes(cuenta));
    setCarrito([]);
    setProducto(null);
    setResultado(null);
    setMensaje('');
    setOrdenConsulta('');
  }, [cuenta]);

  useEffect(() => {
    let vigente = true;
    setCargando(true);
    setErrorCatalogo('');
    checkoutApi.catalogo(busqueda, pagina)
      .then(data => {
        if (!vigente) return;
        setProductos((data.items ?? []).filter(activo));
        setTotal(data.total ?? 0);
      })
      .catch(error => {
        if (!vigente) return;
        setProductos([]);
        setTotal(0);
        setErrorCatalogo(error instanceof Error ? error.message : 'Error al consultar el catálogo.');
      })
      .finally(() => { if (vigente) setCargando(false); });
    return () => { vigente = false; };
  }, [busqueda, pagina]);

  const consultarOrden = useCallback(async (id: string) => {
    if (!esGuid(id)) {
      setErrorSeguimiento('Ingresa un identificador de orden válido.');
      return;
    }
    setConsultando(true);
    setErrorSeguimiento('');
    setResultado(null);
    try {
      const dato = await checkoutApi.seguimiento(id);
      setResultado(dato);
      setOrdenConsulta(id);
      const lista = recordarOrden(cuenta, id);
      setOrdenes(lista);
    } catch (error) {
      const texto = error instanceof ApiError && error.status === 404
        ? 'La orden no existe o no pertenece a tu cuenta.'
        : error instanceof Error ? error.message : 'No fue posible consultar la orden.';
      setErrorSeguimiento(texto);
    } finally {
      setConsultando(false);
    }
  }, [cuenta]);

  // Detecta incluso registros V1 ilegibles o operaciones V2 en otra pestaña.
  const bloqueado = !!pendiente || !!pendienteV2 || hayOperacionPendiente(cuenta);

  useEffect(() => {
    const actualizadoEnOtraPestana = (event: StorageEvent) => {
      if (event.key === null ||
          event.key.startsWith('logimkt_checkout_b2c_v1:') ||
          event.key.startsWith('logimkt_checkout_b2c_v2_qa:') ||
          event.key.startsWith('logimkt_checkout_b2c_v2:')) {
        setPendiente(leerPendiente(cuenta));
        setPendienteV2(leerPendienteV2(cuenta));
        avisarCambioStorage(n => n + 1);
      }
    };
    window.addEventListener('storage', actualizadoEnOtraPestana);
    return () => window.removeEventListener('storage', actualizadoEnOtraPestana);
  }, [cuenta]);

  const confirmar = async () => {
    if (!envioHabilitado || !cuenta || !producto || procesando || envioActivo.current) return;
    if (hayOperacionPendiente(cuenta)) {
      toast.warning('Operación anterior sin cerrar',
        'Existe un intento pendiente V1/V2 o ilegible. Verifica su estado antes de otra compra.');
      return;
    }
    if (!esGuid(producto.id) || !activo(producto) || !Number.isSafeInteger(cantidad) || cantidad < 1 || cantidad > 9999) {
      toast.warning('Selección inválida', 'Selecciona un producto activo y una cantidad entre 1 y 9999.');
      return;
    }
    if (!window.confirm(`¿Confirmas solicitar ${cantidad} unidad(es) de ${producto.nombre}? El servidor calculará el total oficial y puede procesar un cobro si existe una pasarela habilitada.`)) return;

    // Persistencia ANTES del POST, incluso ante pérdida posterior de conexión.
    const nuevo: CheckoutPendiente = {
      clave: nuevaClaveIdempotencia(),
      productoId: producto.id,
      productoNombre: producto.nombre,
      cantidad,
      creadoUtc: new Date().toISOString(),
    };
    try {
      guardarPendiente(cuenta, nuevo);
    } catch {
      toast.error('No se pudo proteger la compra', 'El navegador no permite guardar la clave de seguridad. No se ha enviado la compra.');
      return;
    }
    envioActivo.current = true;
    setPendiente(nuevo);
    setProcesando(true);
    setMensaje('');
    setResultado(null);
    try {
      const response = await checkoutApi.comprar(nuevo.productoId, nuevo.cantidad, nuevo.clave);
      const id = response.ordenId;
      if (esGuid(id)) {
        const lista = recordarOrden(cuenta, id);
        setOrdenes(lista);
        setOrdenConsulta(id);
        borrarPendiente(cuenta, nuevo.clave);
        setPendiente(null);
        setMensaje(`Solicitud aprobada por el servidor. Orden ${id}. El despacho sigue siendo un proceso separado.`);
        toast.success('Checkout aprobado', 'Consulta el seguimiento para revisar el estado de tu pedido.');
        await consultarOrden(id);
      } else {
        setMensaje('La API respondió sin un identificador válido de orden. No vuelvas a enviar la compra; solicita revisión.');
      }
    } catch (error) {
      const apiError = error instanceof ApiError ? error : null;
      const datos = (apiError?.data ?? {}) as CheckoutErrorResponse;
      const id = typeof datos.ordenId === 'string' && esGuid(datos.ordenId) ? datos.ordenId : undefined;
      if (id) {
        const actualizado = { ...nuevo, ordenId: id };
        setPendiente(actualizado);
        try {
          actualizarPendiente(cuenta, actualizado);
          setOrdenes(recordarOrden(cuenta, id));
          setOrdenConsulta(id);
        } catch { /* La clave original ya estaba persistida: bloquear de todos modos. */ }
      }

      // 400 significa que la API rechazó la solicitud sin iniciar checkout.
      // 409 solo es definitivamente rechazado si la API identifica el estado conocido.
      const rechazoDefinitivo = apiError?.status === 400 ||
        (apiError?.status === 409 && ['StockInsuficiente', 'PagoRechazado'].includes(datos.tipo ?? ''));
      if (rechazoDefinitivo) {
        try { borrarPendiente(cuenta, nuevo.clave); setPendiente(null); } catch { /* Preferir bloqueo si localStorage falla. */ }
        setMensaje(datos.mensaje || apiError?.message || 'La solicitud fue rechazada por el servidor.');
      } else {
        setMensaje(id
          ? `La operación requiere verificación (orden ${id}). Consulta su seguimiento. No se ejecutará otro cobro automáticamente.`
          : 'No se confirmó el resultado de la operación. Se conservará la clave original: no envíes otra compra hasta aclarar su estado con administración.');
      }
    } finally {
      envioActivo.current = false;
      setProcesando(false);
    }
  };

  const agregarAlCarrito = () => {
    if (!producto || bloqueado || procesando) return;
    if (!esGuid(producto.id) || !activo(producto) ||
        !esGuid(producto.vendedorId) ||
        !Number.isSafeInteger(cantidad) || cantidad < 1 || cantidad > 9999) {
      toast.warning('Selección inválida', 'Selecciona un producto activo y una cantidad válida.');
      return;
    }
    if (carrito.length > 0 && carrito[0].producto.vendedorId !== producto.vendedorId) {
      toast.warning('Un vendedor por orden', 'El checkout actual admite productos de un mismo vendedor. Vacía el carrito para cambiar de vendedor.');
      return;
    }
    const existente = carrito.find(x => x.producto.id === producto.id);
    const nuevaCantidad = (existente?.cantidad ?? 0) + cantidad;
    if (nuevaCantidad > 9999) {
      toast.warning('Cantidad excedida', 'Cada SKU admite hasta 9,999 unidades por orden.');
      return;
    }
    if (!existente && carrito.length >= 20) {
      toast.warning('Carrito completo', 'El checkout multi-SKU admite un máximo de 20 productos distintos.');
      return;
    }
    setCarrito(actual => existente
      ? actual.map(x => x.producto.id === producto.id ? { ...x, cantidad: nuevaCantidad } : x)
      : [...actual, { producto, cantidad }]);
    toast.success('Producto agregado', 'Puedes seguir agregando productos del mismo vendedor.');
  };

  const quitarDelCarrito = (productoId: string) =>
    setCarrito(actual => actual.filter(x => x.producto.id !== productoId));

  const confirmarCarrito = async () => {
    if (!envioHabilitado || !cuenta || carrito.length < 2 || procesando || envioActivo.current) return;
    if (hayOperacionPendiente(cuenta)) {
      toast.warning('Operación anterior sin cerrar',
        'Existe un intento V1/V2 pendiente o ilegible. Verifica su estado antes de otra compra.');
      return;
    }

    const vendedorIds = new Set(carrito.map(x => x.producto.vendedorId));
    const items: CheckoutMultiSkuItem[] = carrito
      .map(x => ({ productoId: x.producto.id, cantidad: x.cantidad }))
      .sort((a, b) => a.productoId.localeCompare(b.productoId));
    if (vendedorIds.size !== 1 || carrito.length > 20 ||
        items.some(x => !esGuid(x.productoId) || !Number.isSafeInteger(x.cantidad) ||
          x.cantidad < 1 || x.cantidad > 9999)) {
      toast.warning('Carrito inválido', 'Revisa los productos, cantidades y vendedor antes de continuar.');
      return;
    }

    const unidades = items.reduce((suma, item) => suma + item.cantidad, 0);
    const totalEstimado = carrito.reduce((suma, item) =>
      suma + item.producto.precioB2C * item.cantidad, 0);
    if (!window.confirm(`¿Confirmas ${items.length} SKU (${unidades} unidades), con total referencial de ${money(totalEstimado)}? El servidor recalculará los precios y puede procesar un pago si existe una pasarela habilitada.`))
      return;

    const nuevo: CheckoutPendienteV2 = {
      clave: nuevaClaveIdempotencia(),
      items,
      creadoUtc: new Date().toISOString(),
    };
    try {
      guardarPendienteV2(cuenta, nuevo);
    } catch {
      toast.error('No se pudo proteger la compra', 'No se guardó la clave de seguridad. No se ha enviado el carrito.');
      return;
    }

    envioActivo.current = true;
    setPendienteV2(nuevo);
    setProcesando(true);
    setMensaje('');
    setResultado(null);
    try {
      const response = await checkoutApi.comprarMultiSku(items, nuevo.clave);
      const id = response.ordenId;
      if (esGuid(id)) {
        setOrdenes(recordarOrden(cuenta, id));
        setOrdenConsulta(id);
        borrarPendienteV2(cuenta, nuevo.clave);
        setPendienteV2(null);
        setCarrito([]);
        setMensaje(`Checkout multi-SKU aprobado. Orden ${id}. El despacho sigue siendo un proceso separado.`);
        toast.success('Checkout multi-SKU aprobado', 'Consulta el seguimiento para revisar el estado del pedido.');
        await consultarOrden(id);
      } else {
        setMensaje('La API respondió sin un identificador válido de orden. No vuelvas a enviar el carrito; solicita revisión.');
      }
    } catch (error) {
      const apiError = error instanceof ApiError ? error : null;
      const datos = (apiError?.data ?? {}) as CheckoutErrorResponse;
      const id = typeof datos.ordenId === 'string' && esGuid(datos.ordenId) ? datos.ordenId : undefined;
      if (id) {
        const actualizado = { ...nuevo, ordenId: id };
        setPendienteV2(actualizado);
        try {
          actualizarPendienteV2(cuenta, actualizado);
          setOrdenes(recordarOrden(cuenta, id));
          setOrdenConsulta(id);
        } catch { /* Mantener el bloqueo aunque falle la actualización local. */ }
      }

      const rechazoDefinitivo = apiError?.status === 400 ||
        (apiError?.status === 409 &&
          ['StockInsuficiente', 'PagoRechazado', 'CatalogoNoApto'].includes(datos.tipo ?? ''));
      if (rechazoDefinitivo) {
        try { borrarPendienteV2(cuenta, nuevo.clave); setPendienteV2(null); }
        catch { /* Preferir bloqueo si el almacenamiento cambió en otra pestaña. */ }
        setMensaje(datos.mensaje || apiError?.message || 'El carrito fue rechazado por el servidor.');
      } else {
        setMensaje(id
          ? `La operación multi-SKU requiere verificación (orden ${id}). No se repetirá el cobro automáticamente.`
          : 'No se confirmó el resultado del carrito. Se conservará la clave original; no vuelvas a enviarlo hasta aclarar su estado.');
      }
    } finally {
      envioActivo.current = false;
      setProcesando(false);
    }
  };

  const cerrarPendienteV2Resuelto = () => {
    if (!pendienteV2 || !resultado || resultado.ordenId !== pendienteV2.ordenId ||
        !pendienteResuelto(resultado.estadoOrden, resultado.estadoPago,
          resultado.estadoReembolso, resultado.cantidadReservadaPendiente,
          resultado.requiereRevision)) return;
    try {
      borrarPendienteV2(cuenta, pendienteV2.clave);
      setPendienteV2(null);
      setMensaje('Operación multi-SKU verificada y cerrada. Ya puedes iniciar otra compra.');
    } catch {
      toast.error('No se pudo cerrar', 'El navegador no permitió actualizar el registro de seguridad.');
    }
  };

  const cerrarPendienteResuelto = () => {
    if (!pendiente || !resultado || resultado.ordenId !== pendiente.ordenId ||
        !pendienteResuelto(resultado.estadoOrden, resultado.estadoPago,
          resultado.estadoReembolso, resultado.cantidadReservadaPendiente,
          resultado.requiereRevision)) return;
    try {
      borrarPendiente(cuenta, pendiente.clave);
      setPendiente(null);
      setMensaje('Operación anterior verificada y cerrada. Ya puedes iniciar una compra distinta.');
    } catch {
      toast.error('No se pudo cerrar', 'El navegador no permitió actualizar el registro de seguridad.');
    }
  };

  const paginas = Math.max(1, Math.ceil(total / 8));
  const precioEstimado = useMemo(() => producto ? producto.precioB2C * cantidad : 0, [producto, cantidad]);
  const precioCarrito = useMemo(() => carrito.reduce(
    (suma, item) => suma + item.producto.precioB2C * item.cantidad, 0), [carrito]);
  const unidadesCarrito = carrito.reduce((suma, item) => suma + item.cantidad, 0);
  const desbloqueable = !!(pendiente && resultado && pendiente.ordenId === resultado.ordenId &&
    pendienteResuelto(resultado.estadoOrden, resultado.estadoPago,
      resultado.estadoReembolso, resultado.cantidadReservadaPendiente,
      resultado.requiereRevision));
  const desbloqueableV2 = !!(pendienteV2 && resultado && pendienteV2.ordenId === resultado.ordenId &&
    pendienteResuelto(resultado.estadoOrden, resultado.estadoPago,
      resultado.estadoReembolso, resultado.cantidadReservadaPendiente,
      resultado.requiereRevision));

  return (
    <div className="ck-page">
      <header className="ck-heading">
        <div>
          <h1><ShoppingBag size={23} /> Comprar en LOGIMKT</h1>
          <p>Consulta el catálogo y compra un producto o un carrito B2C de hasta 20 SKU del mismo vendedor.</p>
        </div>
        <span className="ck-safe"><ShieldCheck size={15} /> Compra con clave única</span>
      </header>

      <div className="ck-banner"><AlertCircle size={18} />
        <span>El servidor valida precio y existencias. Los pagos solo podrán aprobarse si la pasarela está habilitada en el entorno. Un pago incierto nunca se reintenta automáticamente. {envioHabilitado ? 'El envío está habilitado para este entorno.' : 'Las compras están deshabilitadas hasta configurar y validar una pasarela de pagos.'}</span>
      </div>

      {bloqueado && !pendiente && !pendienteV2 && <section className="ck-card ck-warning" aria-live="polite">
        <h2><AlertCircle size={18} /> Operación guardada sin datos legibles</h2>
        <p>Se detectó una operación V1 o V2 aún sin conciliar, o no se pudo verificar el almacenamiento del navegador. Para evitar otro cobro, esta pantalla bloquea nuevas compras. Solicita revisión con administración; no borres manualmente el registro de seguridad.</p>
      </section>}

      {pendiente && <section className="ck-card ck-warning" aria-live="polite">
        <h2><AlertCircle size={18} /> Operación anterior pendiente de verificación</h2>
        <p><strong>{pendiente.productoNombre}</strong> · {pendiente.cantidad} unidad(es) · Iniciada: {new Date(pendiente.creadoUtc).toLocaleString('es-SV')}</p>
        <p>Por seguridad no se puede iniciar otra compra desde esta cuenta hasta confirmar el estado de la anterior.</p>
        <div className="ck-actions">
          {pendiente.ordenId && <button type="button" className="btn btn-secondary" disabled={consultando} onClick={() => void consultarOrden(pendiente.ordenId!)}>
            <RefreshCw size={15} /> Consultar operación
          </button>}
          {desbloqueable && <button type="button" className="btn btn-primary" onClick={cerrarPendienteResuelto}>
            <CheckCircle2 size={15} /> Cerrar operación verificada
          </button>}
        </div>
        {!pendiente.ordenId && <p className="ck-small">No se recibió un número de orden. Comunícate con administración y conserva esta operación; no la envíes nuevamente.</p>}
      </section>}

      {pendienteV2 && <section className="ck-card ck-warning" aria-live="polite">
        <h2><AlertCircle size={18} /> Operación multi-SKU pendiente de verificación</h2>
        <p><strong>{pendienteV2.items.length} SKU</strong> · {pendienteV2.items.reduce((suma, item) => suma + item.cantidad, 0)} unidad(es) · Iniciada: {new Date(pendienteV2.creadoUtc).toLocaleString('es-SV')}</p>
        <p>Las reservas se conservan mientras el resultado del pago sea incierto. No se enviará otro cobro con esta operación.</p>
        <div className="ck-actions">
          {pendienteV2.ordenId && <button type="button" className="btn btn-secondary" disabled={consultando} onClick={() => void consultarOrden(pendienteV2.ordenId!)}>
            <RefreshCw size={15} /> Consultar operación
          </button>}
          {desbloqueableV2 && <button type="button" className="btn btn-primary" onClick={cerrarPendienteV2Resuelto}>
            <CheckCircle2 size={15} /> Cerrar operación verificada
          </button>}
        </div>
        {!pendienteV2.ordenId && <p className="ck-small">No se recibió un número de orden. Conserva esta clave y solicita revisión administrativa.</p>}
      </section>}

      {mensaje && <div className="ck-message" role="status">{mensaje}</div>}

      <div className="ck-layout">
        <section className="ck-card">
          <div className="ck-section-head"><h2>Catálogo disponible</h2><span>Stock validado por servidor</span></div>
          <form className="ck-search" onSubmit={e => { e.preventDefault(); setPagina(1); setBusqueda(entradaBusqueda.trim()); }}>
            <input className="form-input" value={entradaBusqueda} placeholder="Buscar producto o SKU" aria-label="Buscar productos" onChange={e => setEntradaBusqueda(e.target.value)} maxLength={120} />
            <button type="submit" className="btn btn-secondary"><Search size={15} /> Buscar</button>
          </form>
          {errorCatalogo && <div className="ck-error" role="alert">{errorCatalogo}</div>}
          {cargando ? <div className="ck-loading"><Loader2 size={18} className="ck-spin" /> Consultando catálogo...</div>
            : productos.length === 0 ? <div className="ck-empty">No hay productos comerciales disponibles para este filtro.</div>
            : <div className="ck-products">{productos.map(p => <button type="button" className={`ck-product ${producto?.id === p.id ? 'ck-product-selected' : ''}`} key={p.id}
              disabled={bloqueado || procesando} onClick={() => { setProducto(p); setCantidad(1); }}>
              <span className="ck-product-title">{p.nombre}</span><span className="ck-product-meta">{p.sku} · {p.categoriaNombre}</span>
              <span className="ck-product-price">{money(p.precioB2C)} <small>USD / {p.unidadMedida || 'unidad'}</small></span>
            </button>)}</div>}
          <div className="ck-pages"><span>Página {pagina} de {paginas} · {total} productos encontrados</span><div>
            <button type="button" className="btn btn-secondary btn-sm" disabled={pagina <= 1 || cargando} onClick={() => setPagina(n => n - 1)}>Anterior</button>
            <button type="button" className="btn btn-secondary btn-sm" disabled={pagina >= paginas || cargando} onClick={() => setPagina(n => n + 1)}>Siguiente</button>
          </div></div>
        </section>

        <section className="ck-card ck-summary">
          <div className="ck-section-head"><h2><CreditCard size={17} /> Resumen de compra</h2></div>
          {producto ? <>
            <strong>{producto.nombre}</strong><span className="ck-small">{producto.sku}</span>
            <label htmlFor="ck-cantidad" className="form-label">Cantidad</label>
            <input id="ck-cantidad" className="form-input" type="number" min={1} max={9999} step={1} value={cantidad}
              disabled={bloqueado || procesando} onChange={e => setCantidad(e.target.value === '' ? 0 : Number(e.target.value))} />
            <div className="ck-line"><span>Precio unitario referencial</span><strong>{money(producto.precioB2C)}</strong></div>
            <div className="ck-total"><span>Total estimado</span><strong>{Number.isFinite(precioEstimado) ? money(precioEstimado) : '—'}</strong></div>
            <p className="ck-small">El importe definitivo se calcula en la API; no se envían precios desde el navegador.</p>
            <button type="button" className="btn btn-secondary ck-buy" onClick={agregarAlCarrito} disabled={bloqueado || procesando || !Number.isSafeInteger(cantidad) || cantidad < 1 || cantidad > 9999}>
              <ShoppingCart size={16} /> Agregar al carrito
            </button>
            {carrito.length === 0 && <button type="button" className="btn btn-primary ck-buy" onClick={() => void confirmar()} disabled={!envioHabilitado || bloqueado || procesando || !Number.isSafeInteger(cantidad) || cantidad < 1 || cantidad > 9999}>
              {procesando ? <><Loader2 className="ck-spin" size={16} /> Procesando...</> : <><ShieldCheck size={16} /> {envioHabilitado ? 'Confirmar compra individual' : 'Pago no habilitado'}</>}
            </button>}
          </> : <p className="ck-empty">Selecciona un producto del catálogo para comenzar.</p>}

          {carrito.length > 0 && <div className="ck-cart-block">
            <div className="ck-section-head">
              <h3><ShoppingCart size={17} /> Carrito multi-SKU</h3>
              <span>{carrito.length}/20 SKU · {unidadesCarrito} unidades</span>
            </div>
            {carrito.map(item => <div className="ck-line" key={item.producto.id}>
              <span>{item.producto.nombre} · {item.producto.sku} × {item.cantidad}</span>
              <strong>{money(item.producto.precioB2C * item.cantidad)}</strong>
              <button type="button" className="btn btn-secondary btn-sm"
                aria-label={`Quitar ${item.producto.nombre}`}
                disabled={bloqueado || procesando}
                onClick={() => quitarDelCarrito(item.producto.id)}>
                <Trash2 size={14} /> Quitar
              </button>
            </div>)}
            <div className="ck-total"><span>Total referencial del carrito</span><strong>{money(precioCarrito)}</strong></div>
            {carrito.length < 2 && <p className="ck-small">Agrega otro producto del mismo vendedor para habilitar el checkout multi-SKU.</p>}
            <button type="button" className="btn btn-primary ck-buy" onClick={() => void confirmarCarrito()}
              disabled={!envioHabilitado || bloqueado || procesando || carrito.length < 2}>
              {procesando ? <><Loader2 className="ck-spin" size={16} /> Procesando...</> : <><ShieldCheck size={16} /> {envioHabilitado ? `Comprar ${carrito.length} SKU` : 'Pago no habilitado'}</>}
            </button>
            <p className="ck-small">El navegador envía únicamente los identificadores y cantidades. El servidor vuelve a validar vendedor, precio, stock y total.</p>
          </div>}
        </section>
      </div>

      <section className="ck-card" id="ck-seguimiento">
        <div className="ck-section-head"><h2><ClipboardList size={18} /> Seguimiento de órdenes</h2></div>
        <p className="ck-small">Consulta una orden propia mediante su identificador. Los IDs mostrados son accesos guardados en este navegador, no un historial completo del servidor.</p>
        <form className="ck-search" onSubmit={e => { e.preventDefault(); void consultarOrden(ordenConsulta.trim()); }}>
          <input className="form-input" placeholder="Identificador de orden (UUID)" value={ordenConsulta} onChange={e => setOrdenConsulta(e.target.value)} aria-label="Identificador de orden" />
          <button type="submit" className="btn btn-secondary" disabled={consultando}>{consultando ? <Loader2 size={15} className="ck-spin" /> : <RefreshCw size={15} />} Consultar</button>
        </form>
        {ordenes.length > 0 && <div className="ck-order-links">{ordenes.map(id => <button key={id} className="ck-order-link" disabled={consultando} type="button" onClick={() => void consultarOrden(id)}>{id}</button>)}</div>}
        {errorSeguimiento && <div className="ck-error" role="alert">{errorSeguimiento}</div>}
        {resultado && <div className="ck-track" aria-live="polite">
          <div className="ck-track-title"><strong>{resultado.numeroOrden}</strong><span>{money(resultado.montoTotal)} {resultado.moneda}</span></div>
          <div className="ck-track-grid">
            <div><span>Orden</span><strong>{resultado.estadoOrden}</strong></div>
            <div><span>Pago</span><strong>{resultado.estadoPago}</strong></div>
            <div><span>Reserva WMS</span><strong>{resultado.estadoReserva}</strong></div>
            <div><span>Reembolso</span><strong>{resultado.estadoReembolso ?? 'Sin reembolso'}</strong></div>
            <div><span>Pendiente de despacho</span><strong>{resultado.cantidadReservadaPendiente}</strong></div>
            <div><span>Despachado</span><strong>{resultado.cantidadDespachada}</strong></div>
          </div>
          {resultado.requiereRevision && <div className="ck-warning-inline" role="alert"><AlertCircle size={16} /> Requiere revisión administrativa. No intentes otro cobro.</div>}
          {resultado.alertas?.length > 0 && <ul className="ck-alerts">{resultado.alertas.map((a, i) => <li key={`${a}-${i}`}>{etiquetaAlerta(a)}</li>)}</ul>}
        </div>}
      </section>
    </div>
  );
}
