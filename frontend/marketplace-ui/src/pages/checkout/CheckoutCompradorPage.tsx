/**
 * LOGIMKT — Checkout del comprador. Un producto por orden (contrato actual de la API).
 * No automatizar reintentos de cobro. No asumir que el cliente conoce el stock total WMS.
 */
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, ClipboardList, CreditCard, Loader2, RefreshCw, Search, ShieldCheck, ShoppingBag } from 'lucide-react';
import { ApiError } from '../../api/client';
import { checkoutApi, type CheckoutErrorResponse, type CheckoutSeguimiento } from '../../api/checkout';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import type { Producto } from '../../types';
import {
  borrarPendiente, esGuid, guardarPendiente, leerOrdenes, leerPendiente,
  nuevaClaveIdempotencia, pendienteResuelto, recordarOrden, type CheckoutPendiente,
} from './checkoutState';
import './checkout.css';

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
  const [procesando, setProcesando] = useState(false);
  const [pendiente, setPendiente] = useState<CheckoutPendiente | null>(() => leerPendiente(cuenta));
  const [ordenes, setOrdenes] = useState<string[]>(() => leerOrdenes(cuenta));
  const [mensaje, setMensaje] = useState('');
  const [resultado, setResultado] = useState<CheckoutSeguimiento | null>(null);
  const [consultando, setConsultando] = useState(false);
  const [ordenConsulta, setOrdenConsulta] = useState('');
  const [errorSeguimiento, setErrorSeguimiento] = useState('');

  // Cambios de sesión: no mostrar historial ni intento pendiente de otro comprador.
  useEffect(() => {
    setPendiente(leerPendiente(cuenta));
    setOrdenes(leerOrdenes(cuenta));
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

  const confirmar = async () => {
    if (!envioHabilitado || !cuenta || !producto || procesando || envioActivo.current || pendiente) return;
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
        borrarPendiente(cuenta);
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
          guardarPendiente(cuenta, actualizado);
          setOrdenes(recordarOrden(cuenta, id));
          setOrdenConsulta(id);
        } catch { /* La clave original ya estaba persistida: bloquear de todos modos. */ }
      }

      // 400 significa que la API rechazó la solicitud sin iniciar checkout.
      // 409 solo es definitivamente rechazado si la API identifica el estado conocido.
      const rechazoDefinitivo = apiError?.status === 400 ||
        (apiError?.status === 409 && ['StockInsuficiente', 'PagoRechazado'].includes(datos.tipo ?? ''));
      if (rechazoDefinitivo) {
        try { borrarPendiente(cuenta); setPendiente(null); } catch { /* Preferir bloqueo si localStorage falla. */ }
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

  const cerrarPendienteResuelto = () => {
    if (!pendiente || !resultado || resultado.ordenId !== pendiente.ordenId ||
        !pendienteResuelto(resultado.estadoOrden, resultado.estadoPago,
          resultado.estadoReembolso, resultado.cantidadReservadaPendiente)) return;
    try {
      borrarPendiente(cuenta);
      setPendiente(null);
      setMensaje('Operación anterior verificada y cerrada. Ya puedes iniciar una compra distinta.');
    } catch {
      toast.error('No se pudo cerrar', 'El navegador no permitió actualizar el registro de seguridad.');
    }
  };

  const paginas = Math.max(1, Math.ceil(total / 8));
  const precioEstimado = useMemo(() => producto ? producto.precioB2C * cantidad : 0, [producto, cantidad]);
  const desbloqueable = !!(pendiente && resultado && pendiente.ordenId === resultado.ordenId &&
    pendienteResuelto(resultado.estadoOrden, resultado.estadoPago,
      resultado.estadoReembolso, resultado.cantidadReservadaPendiente));

  return (
    <div className="ck-page">
      <header className="ck-heading">
        <div>
          <h1><ShoppingBag size={23} /> Comprar en LOGIMKT</h1>
          <p>Consulta productos disponibles y realiza compras B2C de un producto por orden.</p>
        </div>
        <span className="ck-safe"><ShieldCheck size={15} /> Compra con clave única</span>
      </header>

      <div className="ck-banner"><AlertCircle size={18} />
        <span>El servidor valida precio y existencias. Los pagos solo podrán aprobarse si la pasarela está habilitada en el entorno. Un pago incierto nunca se reintenta automáticamente. {envioHabilitado ? 'El envío está habilitado para este entorno.' : 'Las compras están deshabilitadas hasta configurar y validar una pasarela de pagos.'}</span>
      </div>

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
              disabled={!!pendiente || procesando} onClick={() => { setProducto(p); setCantidad(1); }}>
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
              disabled={!!pendiente || procesando} onChange={e => setCantidad(e.target.value === '' ? 0 : Number(e.target.value))} />
            <div className="ck-line"><span>Precio unitario referencial</span><strong>{money(producto.precioB2C)}</strong></div>
            <div className="ck-total"><span>Total estimado</span><strong>{Number.isFinite(precioEstimado) ? money(precioEstimado) : '—'}</strong></div>
            <p className="ck-small">El importe definitivo se calcula en la API; no se envían precios desde el navegador.</p>
            <button type="button" className="btn btn-primary ck-buy" onClick={() => void confirmar()} disabled={!envioHabilitado || !!pendiente || procesando || !Number.isSafeInteger(cantidad) || cantidad < 1 || cantidad > 9999}>
              {procesando ? <><Loader2 className="ck-spin" size={16} /> Procesando...</> : <><ShieldCheck size={16} /> {envioHabilitado ? 'Confirmar compra' : 'Pago no habilitado'}</>}
            </button>
          </> : <p className="ck-empty">Selecciona un producto del catálogo para comenzar.</p>}
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
