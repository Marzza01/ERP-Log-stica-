/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { ShoppingBag, Search, Plus, Filter, AlertCircle, RefreshCw, Edit2, Trash2, Printer, Camera } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import CodigoBarrasSvg from '../../components/ui/CodigoBarrasSvg';
import EscanerCamaraModal from './EscanerCamaraModal';
import { esEan13Valido } from '../../utils/ean13';
import { codigosBarrasApi } from '../../api/codigosBarras';
import { useAuth } from '../../context/AuthContext';
import DataTable from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { useToast } from '../../context/ToastContext';
import { productosApi, type CrearProductoRequest, type OpcionesAltaProducto, type OrdenProductos } from '../../api/productos';
import { ApiError } from '../../api/client';
import type { Producto } from '../../types';

export default function ProductosPage() {
  const navigate = useNavigate();
  const { usuario } = useAuth();
  const esAdmin = usuario?.rol === 'Admin';
  const [seleccionados, setSeleccionados] = useState<Set<string>>(() => new Set());
  const [escanerAbierto, setEscanerAbierto] = useState(false);
  const [codigoEditado, setCodigoEditado] = useState('');
  const [asignandoCodigo, setAsignandoCodigo] = useState(false);
  const [productos, setProductos] = useState<Producto[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selectedCat, setSelectedCat] = useState('');
  const [ordenCatalogo, setOrdenCatalogo] = useState<OrdenProductos>('sku');
  const [categoriasFiltro, setCategoriasFiltro] = useState<string[]>([]);
  const [proveedoresDisponibles, setProveedoresDisponibles] = useState<string[]>([]);
  const [errorProveedores, setErrorProveedores] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const lastRequest = useRef(0);
  const PAGE_SIZE = 50;
  const [errorCarga, setErrorCarga] = useState('');

  // Modal
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [skuOriginal, setSkuOriginal] = useState('');
  const [validacionSku, setValidacionSku] = useState<{
    sku: string;
    editingId: string | null;
    estado: 'verificando' | 'disponible' | 'ocupado' | 'error';
  } | null>(null);
  const [form, setForm] = useState<CrearProductoRequest>({
    sku: '',
    nombre: '',
    descripcion: '',
    categoriaNombre: 'Tecnología',
    subcategoria: 'General',
    precioB2C: 0,
    precioB2B: 0,
    stockMinimo: 10,
    proveedorNombre: '',
    paisOrigen: 'El Salvador',
    pesoKg: 0.5,
    dimensionesCm: '20x15x5',
    unidadMedida: 'Unidad',
    estadoProducto: 'Activo',
    estaPublicado: true,
  });
  const [saving, setSaving] = useState(false);
  const [opcionesAlta, setOpcionesAlta] = useState<OpcionesAltaProducto | null>(null);
  const [errorOpciones, setErrorOpciones] = useState('');
  // Confirm delete
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  useEffect(() => {
    let vigente = true;
    productosApi.categoriasDisponibles()
      .then(items => { if (vigente) setCategoriasFiltro(items); })
      .catch(() => { if (vigente) setCategoriasFiltro([]); });
    return () => { vigente = false; };
  }, [modalOpen]);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  const loadData = useCallback(async () => {
    const requestId = ++lastRequest.current;
    setLoading(true);
    setErrorCarga('');
    try {
      const res = debouncedSearch || selectedCat
        ? await productosApi.buscar({ q: debouncedSearch || undefined, categoria: selectedCat || undefined, pagina: page, tamano: PAGE_SIZE, orden: ordenCatalogo })
        : await productosApi.listar(page, PAGE_SIZE, ordenCatalogo);
      if (requestId !== lastRequest.current) return;
      setProductos(res.items ?? []);
      setTotal(res.total ?? 0);
    } catch (error) {
      if (requestId !== lastRequest.current) return;
      setProductos([]);
      setTotal(0);
      setErrorCarga(error instanceof Error ? error.message : 'No se pudo cargar el catálogo desde la API.');
    } finally {
      if (requestId === lastRequest.current) setLoading(false);
    }
  }, [debouncedSearch, selectedCat, ordenCatalogo, page]);

  useEffect(() => { void loadData(); return () => { lastRequest.current++; }; }, [loadData]);

  // Datos reales desde Core, solo para ALTA. No inventar GUID y no
  // exigir modificar categorías/vendedores al editar productos existentes.
  useEffect(() => {
    if (!modalOpen) return;
    let vigente = true;
    setOpcionesAlta(null);
    setErrorOpciones('');
    setProveedoresDisponibles([]);
    setErrorProveedores('');
    void Promise.all([productosApi.opcionesAlta(), productosApi.proveedoresDisponibles()])
      .then(([ref, proveedores]) => {
        if (!vigente) return;
        setOpcionesAlta(ref);
        setProveedoresDisponibles(proveedores);
        if (!editingId) setForm(f => ({
          ...f,
          vendedorId: ref.vendedores.length === 1 ? ref.vendedores[0].id : '',
          categoriaId: '',
        }));
      }).catch(e => {
        if (!vigente) return;
        setErrorOpciones(e instanceof Error ? e.message : 'No se pudieron verificar las referencias de Core.');
        setErrorProveedores('No se pudo cargar el maestro de proveedores.');
      });
    return () => { vigente = false; };
  }, [modalOpen, editingId]);

  // Nunca confiar en la página del catálogo (solo contiene hasta 50 productos).
  // Consultar la API al escribir, ignorar respuestas obsoletas y no considerar
  // disponible un SKU cuya verificación falló por red o servidor.
  useEffect(() => {
    if (!modalOpen) return;
    const sku = form.sku.trim();
    if (!sku || sku.length > 50) {
      setValidacionSku(null);
      return;
    }
    if (editingId && form.sku === skuOriginal) {
      // Conservar el propio SKU, aun si hay duplicados históricos previos.
      setValidacionSku({ sku, editingId, estado: 'disponible' });
      return;
    }
    let vigente = true;
    setValidacionSku({ sku, editingId, estado: 'verificando' });
    const timer = window.setTimeout(() => {
      void productosApi.skuDisponible(sku, editingId ?? undefined)
        .then(({ disponible }) => {
          if (vigente) setValidacionSku({ sku, editingId, estado: disponible ? 'disponible' : 'ocupado' });
        })
        .catch(() => {
          if (vigente) setValidacionSku({ sku, editingId, estado: 'error' });
        });
    }, 350);
    return () => { vigente = false; window.clearTimeout(timer); };
  }, [modalOpen, form.sku, editingId, skuOriginal]);

  const skuActual = form.sku.trim();
  const skuDisponibleConfirmado = !!skuActual && skuActual.length <= 50 &&
    validacionSku?.sku === skuActual && validacionSku?.editingId === editingId &&
    validacionSku.estado === 'disponible';
  const estadoSkuActual = validacionSku?.sku === skuActual && validacionSku?.editingId === editingId
    ? validacionSku.estado : 'verificando';
  const vendedorAltaValido = !!opcionesAlta?.vendedores.some(v => v.id === form.vendedorId);
  const categoriaAltaValida = !!opcionesAlta?.categorias.some(c => c.id === form.categoriaId);
  const puedeGuardar = !saving && skuDisponibleConfirmado &&
    (!!editingId || (vendedorAltaValido && categoriaAltaValida));
  const motivoBloqueo = !skuActual ? 'Ingresa el código SKU.' :
    estadoSkuActual === 'ocupado' ? 'El SKU ya está registrado.' :
    estadoSkuActual === 'error' ? 'No se pudo verificar el SKU; revisa la conexión.' :
    !skuDisponibleConfirmado ? 'Verificando la disponibilidad del SKU...' :
    !editingId && !opcionesAlta ? 'No se han cargado las referencias de Core.' :
    !editingId && !vendedorAltaValido ? 'Selecciona un vendedor activo.' :
    !editingId && !opcionesAlta?.categorias.length ? 'No hay categorías registradas en Core. Solicita su configuración.' :
    !editingId && !categoriaAltaValida ? 'Selecciona una categoría.' : '';

  const totalDisponibles = productos.reduce((acc, p) => acc + (p.stock?.cantidadDisponible ?? 0), 0);
  const totalReservados = productos.reduce((acc, p) => acc + (p.stock?.cantidadReservada ?? 0), 0);
  const skusCriticos = productos.filter(p => (p.stock?.cantidadDisponible ?? 0) < 10).length;

  const handleOpenCrear = () => {
    setEditingId(null);
    setSkuOriginal('');
    setValidacionSku(null);
    setCodigoEditado('');
    setForm({
      sku: '',
      nombre: '',
      descripcion: '',
      categoriaNombre: '',
      subcategoria: 'General',
      precioB2C: 0,
      precioB2B: 0,
      stockMinimo: 10,
      proveedorNombre: '',
      paisOrigen: 'El Salvador',
      pesoKg: 0.5,
      dimensionesCm: '20x15x5',
      unidadMedida: 'Unidad',
      estadoProducto: 'Activo',
      estaPublicado: true,
      vendedorId: '',
      categoriaId: '',
    });
    setModalOpen(true);
  };

  const handleOpenEditar = (p: Producto) => {
    setEditingId(p.id);
    setSkuOriginal(p.sku);
    setValidacionSku(null);
    setCodigoEditado(p.codigoEAN ?? '');
    setForm({
      sku: p.sku,
      nombre: p.nombre,
      descripcion: p.descripcion ?? '',
      categoriaNombre: p.categoriaNombre,
      subcategoria: p.subcategoria,
      precioB2C: p.precioB2C,
      precioB2B: p.precioB2B,
      stockMinimo: p.stockMinimo,
      proveedorNombre: p.proveedorNombre,
      paisOrigen: p.paisOrigen,
      pesoKg: p.pesoKg,
      dimensionesCm: p.dimensionesCm,
      unidadMedida: p.unidadMedida,
      estadoProducto: p.estadoProducto,
      estaPublicado: p.estaPublicado,
    });
    setModalOpen(true);
  };

  const toast = useToast();

  const handleAsignarCodigo = async () => {
    if (!editingId || !esAdmin || codigoEditado) return;
    if (!window.confirm('¿Asignar un código EAN interno a este producto sin código? Se conservarán todos los EAN existentes.')) return;
    setAsignandoCodigo(true);
    try {
      const res = await codigosBarrasApi.asignarSiFalta(editingId);
      setCodigoEditado(res.codigoEAN);
      toast.success('Código interno asignado', 'EAN-13: ' + res.codigoEAN);
      await loadData();
    } catch (error) {
      toast.error('No se pudo asignar el código',
        error instanceof Error ? error.message : 'Verifica que la migración de Desarrollo esté aplicada.');
    } finally {
      setAsignandoCodigo(false);
    }
  };

  const handleSave = async () => {
    // Enter y clic siguen la misma barrera: no enviar sin verificación vigente.
    if (saving || !skuDisponibleConfirmado) return;
    setSaving(true);
    try {
      if (!editingId && !proveedoresDisponibles.includes(form.proveedorNombre))
        throw new Error('Selecciona un proveedor activo registrado en Core.');
      if (editingId) {
        await productosApi.actualizar(editingId, form);
        toast.success('Producto Actualizado', `El SKU ${form.sku} se actualizó correctamente.`);
      } else {
        if (!opcionesAlta ||
            !opcionesAlta.vendedores.some(v => v.id === form.vendedorId) ||
            !opcionesAlta.categorias.some(c => c.id === form.categoriaId))
          throw new Error('Selecciona vendedor y categoría disponibles desde Core.');
        await productosApi.crear(form);
        toast.success('Producto Creado', `El SKU ${form.sku} se agregó al catálogo comercial.`);
      }
      await loadData();
      setModalOpen(false);
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && error.data?.codigo === 'SKU_DUPLICADO') {
        setValidacionSku({ sku: skuActual, editingId, estado: 'ocupado' });
      } else {
        const mensaje = error instanceof Error ? error.message : 'No fue posible guardar el producto.';
        toast.error('Error al guardar producto', mensaje);
      }
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    setConfirmDeleteId(id);
  };

  const handleConfirmDelete = async () => {
    if (!confirmDeleteId) return;
    try {
      await productosApi.eliminar(confirmDeleteId);
      toast.warning('Producto Dado de Baja', 'El producto ha sido retirado del catálogo.');
      await loadData();
      setConfirmDeleteId(null);
    } catch (error) {
      const mensaje = error instanceof Error ? error.message : 'No fue posible dar de baja el producto.';
      toast.error('Error al dar de baja', mensaje);
    }
  };

  const alternarEtiqueta = (id: string, activo: boolean) => {
    setSeleccionados(actual => {
      const copia = new Set(actual);
      if (activo && copia.size >= 100 && !copia.has(id)) return actual;
      if (activo) copia.add(id);
      else copia.delete(id);
      return copia;
    });
  };

  const abrirEtiquetasSeleccionadas = () => {
    const ids = [...seleccionados];
    if (ids.length === 0) return;
    navigate('/productos/etiquetas?ids=' + ids.map(encodeURIComponent).join(','));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Catálogo de Productos & SKUs
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Catálogo paginado. Las existencias mostradas corresponden únicamente a los productos de esta página.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
          <button className="btn btn-secondary" type="button" disabled={seleccionados.size === 0}
            onClick={abrirEtiquetasSeleccionadas}>
            <Printer size={15} /> Etiquetas ({seleccionados.size})
          </button>
          <button className="btn btn-secondary" type="button"
            onClick={() => navigate('/productos/etiquetas?todos=1')}>
            Hojas del catálogo
          </button>
          {esAdmin && (
            <button className="btn btn-secondary" type="button"
              onClick={() => navigate('/productos/rotaciones')}>
              Rotaciones EAN (Admin)
            </button>
          )}
          <button
            onClick={loadData}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 0.85rem',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
            }}
          >
            <RefreshCw size={15} />
            Refrescar
          </button>
          <button
            onClick={handleOpenCrear}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 1rem',
              borderRadius: '8px',
              border: 'none',
              background: 'var(--accent)',
              color: '#fff',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            <Plus size={16} />
            Nuevo Producto (SKU)
          </button>
        </div>
      </div>

      {errorCarga && (
        <AlertBanner variant="danger" title="Catálogo no disponible" message={errorCarga} action={{ label: 'Reintentar', onClick: loadData }} />
      )}

      {/* KPI Cards Strip */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1rem' }}>
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total SKUs Registrados</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem' }}>
            {total}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--accent)', marginTop: '0.25rem' }}>Productos publicados según filtros</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Stock Disponible (página)</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--green)', marginTop: '0.35rem' }}>
            {totalDisponibles.toLocaleString()} unid.
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Listo para picking en WMS</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Stock Reservado (página)</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--yellow)', marginTop: '0.35rem' }}>
            {totalReservados.toLocaleString()} unid.
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>En órdenes activas</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Bajo 10 unidades (página)</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: skusCriticos > 0 ? 'var(--red)' : 'var(--green)', marginTop: '0.35rem' }}>
            {skusCriticos} SKUs
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Stock &lt; 10 unidades</div>
        </div>
      </div>

      {/* Filter Bar */}
      <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
        <div style={{ flex: 1, position: 'relative' }}>
          <Search size={16} style={{ position: 'absolute', left: '0.75rem', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-muted)' }} />
          <input
            type="text"
            placeholder="Buscar por SKU o nombre de producto..."
            value={search}
            onChange={e => { setSearch(e.target.value); setPage(1); }}
            style={{
              width: '100%',
              padding: '0.55rem 0.75rem 0.55rem 2.2rem',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              fontSize: '0.875rem',
              boxSizing: 'border-box',
            }}
          />
        </div>

        <button className="btn btn-secondary" type="button"
          onClick={() => setEscanerAbierto(true)}>
          <Camera size={15} /> Escanear cámara
        </button>
        <div style={{ width: '220px' }}>
          <select
            aria-label="Filtrar por categoría registrada"
            value={selectedCat}
            onChange={e => { setSelectedCat(e.target.value); setPage(1); }}
            style={{
              width: '100%', padding: '0.55rem 0.75rem', borderRadius: '8px',
              border: '1px solid var(--border)', background: 'var(--bg-surface)',
              color: 'var(--text-primary)', fontSize: '0.875rem', boxSizing: 'border-box',
            }}
          >
            <option value="">Todas las categorías</option>
            {selectedCat && !categoriasFiltro.includes(selectedCat) && <option value={selectedCat}>{selectedCat}</option>}
            {categoriasFiltro.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
        <div style={{ width: '210px' }}>
          <select
            aria-label="Ordenar catálogo de SKU"
            value={ordenCatalogo}
            onChange={e => { setOrdenCatalogo(e.target.value as OrdenProductos); setPage(1); }}
            style={{
              width: '100%', padding: '0.55rem 0.75rem', borderRadius: '8px',
              border: '1px solid var(--border)', background: 'var(--bg-surface)',
              color: 'var(--text-primary)', fontSize: '0.875rem', boxSizing: 'border-box',
            }}
          >
            <option value="sku">Orden por SKU</option>
            <option value="recientes">Más recientes primero</option>
            <option value="antiguos">Más antiguos primero</option>
          </select>
        </div>
      </div>

      {/* Tabla Productos */}
      <DataTable
        columns={[
          {
            header: 'Etiqueta',
            key: 'etiqueta',
            sortable: false,
            render: (row) => (
              <input type="checkbox" checked={seleccionados.has(row.id)}
                aria-label={'Seleccionar etiqueta de ' + row.sku}
                onChange={e => alternarEtiqueta(row.id, e.target.checked)} />
            ),
          },
          {
            header: 'SKU',
            accessor: 'sku',
            render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.sku}</span>,
          },
          {
            header: 'EAN-13',
            key: 'codigoEAN',
            sortable: false,
            render: (row) => (
              <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.8rem' }}>
                {row.codigoEAN && esEan13Valido(row.codigoEAN) ? row.codigoEAN : 'Sin EAN válido'}
              </span>
            ),
          },
          {
            header: 'Nombre del Producto',
            accessor: 'nombre',
            render: (row) => (
              <div>
                <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{row.nombre}</div>
                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.15rem' }}>{row.proveedorNombre} • {row.categoriaNombre}</div>
              </div>
            ),
          },
          {
            header: 'Precio Venta',
            accessor: 'precio',
            render: (row) => (
              <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--text-primary)' }}>
                ${(Number(row.precioB2C) || 0).toFixed(2)}
              </span>
            ),
          },
          {
            header: 'Stock Disponible',
            accessor: 'stockFisico',
            render: (row) => {
              const disp = row.stock?.cantidadDisponible ?? 0;
              let bg = 'rgba(34, 197, 94, 0.15)';
              let col = 'var(--green)';
              if (disp === 0) {
                bg = 'rgba(239, 68, 68, 0.15)';
                col = 'var(--red)';
              } else if (disp < 10) {
                bg = 'rgba(245, 158, 11, 0.15)';
                col = 'var(--yellow)';
              }
              return (
                <span style={{ padding: '0.2rem 0.6rem', borderRadius: '12px', background: bg, color: col, fontWeight: 700, fontSize: '0.8rem' }}>
                  {disp} unid.
                </span>
              );
            },
          },
          {
            header: 'Stock Reservado',
            key: 'stockReservado',
            render: (row) => (
              <span style={{ color: 'var(--text-secondary)', fontSize: '0.85rem' }}>
                {row.stock?.cantidadReservada ?? 0} unid.
              </span>
            ),
            sortable: false,
          },
          {
            header: 'Acciones',
            accessor: 'id',
            render: (row) => (
              <div style={{ display: 'flex', gap: '0.4rem' }}>
                <button
                  onClick={() => handleOpenEditar(row)}
                  style={{
                    padding: '0.35rem 0.6rem',
                    borderRadius: '6px',
                    border: '1px solid var(--border)',
                    background: 'var(--bg-surface)',
                    color: 'var(--text-primary)',
                    cursor: 'pointer',
                  }}
                  title="Editar"
                >
                  <Edit2 size={13} />
                </button>
                <button
                  onClick={() => handleDelete(row.id)}
                  style={{
                    padding: '0.35rem 0.6rem',
                    borderRadius: '6px',
                    border: '1px solid var(--border)',
                    background: 'var(--bg-surface)',
                    color: 'var(--red)',
                    cursor: 'pointer',
                  }}
                  title="Dar de baja"
                >
                  <Trash2 size={13} />
                </button>
              </div>
            ),
          },
        ]}
        data={productos}
        keyExtractor={(item) => item.id}
        loading={loading}
        pageSize={PAGE_SIZE}
        serverPagination={{ page, total, onPageChange: setPage }}
        emptyText="No se encontraron productos coincidentes."
      />

      <EscanerCamaraModal abierto={escanerAbierto}
        onCerrar={() => setEscanerAbierto(false)}
        onCodigo={codigo => {
          setEscanerAbierto(false);
          setSelectedCat('');
          setPage(1);
          setSearch(codigo);
          toast.success('Código detectado', 'Filtrando el catálogo por EAN-13.');
        }} />

      {/* Modal Crear / Editar */}
      {modalOpen && (
        <Modal
          title={editingId ? 'Editar Producto del Catálogo' : 'Registrar Nuevo Producto (SKU Maestro)'}
          isOpen={true}
          onClose={() => setModalOpen(false)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              handleSave();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            {editingId && (
              <div style={{ padding: '0.5rem', border: '1px solid var(--border)', borderRadius: 8 }}>
                <div style={{ marginBottom: 6, color: 'var(--text-secondary)' }}>Código EAN actual (solo lectura)</div>
                <CodigoBarrasSvg codigo={codigoEditado} alto={60} style={{ maxWidth: '100%' }} />
                <p style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                  El EAN existente no se modifica al editar el producto.
                </p>
                {esAdmin && !codigoEditado && (
                  <button type="button" className="btn btn-secondary"
                    disabled={asignandoCodigo} onClick={() => void handleAsignarCodigo()}>
                    {asignandoCodigo ? 'Asignando...' : 'Asignar EAN-13 interno'}
                  </button>
                )}
              </div>
            )}
            {!editingId && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                {errorOpciones && (
                  <AlertBanner variant="danger" title="Referencias de Core no disponibles" message={errorOpciones} />
                )}
                {!opcionesAlta && !errorOpciones && <p>Verificando vendedores y categorías reales...</p>}
                {opcionesAlta && (
                  <label>
                    Vendedor activo
                    <select required value={form.vendedorId ?? ''}
                      onChange={e => setForm(f => ({ ...f, vendedorId: e.target.value }))}
                      style={{ display: 'block', width: '100%' }}>
                      <option value="">Seleccionar vendedor</option>
                      {opcionesAlta.vendedores.map(v => <option key={v.id} value={v.id}>{v.nombre}</option>)}
                    </select>
                  </label>
                )}
              </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  SKU
                </label>
                <input
                  type="text"
                  value={form.sku}
                  onChange={e => setForm(f => ({ ...f, sku: e.target.value }))}
                  maxLength={50}
                  aria-describedby="mensaje-validacion-sku"
                  aria-invalid={!!skuActual && estadoSkuActual === 'ocupado'}
                  required
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', fontFamily: 'var(--font-mono)', boxSizing: 'border-box' }}
                />
                <div id="mensaje-validacion-sku" aria-live="polite" style={{ marginTop: '0.3rem', fontSize: '0.8rem' }}>
                  {skuActual && estadoSkuActual === 'ocupado' &&
                    <span role="alert" style={{ color: 'var(--red)' }}>Este código SKU ya está registrado. Ingresa uno diferente.</span>}
                  {skuActual && estadoSkuActual === 'error' &&
                    <span role="alert" style={{ color: 'var(--red)' }}>No se pudo verificar el SKU. Comprueba la conexión e inténtalo de nuevo.</span>}
                  {skuActual && estadoSkuActual === 'verificando' &&
                    <span style={{ color: 'var(--text-secondary)' }}>Verificando disponibilidad del SKU...</span>}
                </div>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Nombre del Producto
                </label>
                <input
                  type="text"
                  value={form.nombre}
                  onChange={e => setForm(f => ({ ...f, nombre: e.target.value }))}
                  required
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Precio Venta ($)
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={form.precioB2C}
                  onChange={e => setForm(f => ({ ...f, precioB2C: parseFloat(e.target.value) || 0 }))}
                  required
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Categoría
                </label>
                {editingId ? (
                  <select
                    value={form.categoriaNombre}
                    disabled={!opcionesAlta}
                    onChange={e => setForm(f => ({ ...f, categoriaNombre: e.target.value,
                      categoriaId: opcionesAlta?.categorias.find(c => c.nombre === e.target.value)?.id ?? f.categoriaId }))}
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                  >
                    {!opcionesAlta?.categorias.some(c => c.nombre === form.categoriaNombre) && (
                      <option value={form.categoriaNombre}>{form.categoriaNombre} (valor histórico)</option>
                    )}
                    {opcionesAlta?.categorias.map(c => <option key={c.id} value={c.nombre}>{c.nombre}</option>)}
                  </select>
                ) : (
                  <>
                    <select
                      required
                      value={form.categoriaId ?? ''}
                      disabled={!opcionesAlta || opcionesAlta.categorias.length === 0}
                      onChange={e => {
                        const categoria = opcionesAlta?.categorias.find(c => c.id === e.target.value);
                        setForm(f => ({ ...f, categoriaId: e.target.value,
                          categoriaNombre: categoria?.nombre ?? '' }));
                      }}
                      style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                    >
                      <option value="">Seleccionar categoría</option>
                      {opcionesAlta?.categorias.map(c => <option key={c.id} value={c.id}>{c.nombre}</option>)}
                    </select>
                    {opcionesAlta && opcionesAlta.categorias.length === 0 && (
                      <p role="alert" style={{ color: 'var(--red)', fontSize: 12, marginTop: 4 }}>
                        No hay categorías registradas en Core. No es posible crear un producto hasta configurarlas.
                      </p>
                    )}
                  </>
                )}
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Proveedor
                </label>
                <select
                  required
                  value={form.proveedorNombre}
                  disabled={!opcionesAlta || Boolean(errorProveedores)}
                  onChange={e => setForm(f => ({ ...f, proveedorNombre: e.target.value }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                >
                  <option value="">Seleccionar proveedor registrado</option>
                  {editingId && form.proveedorNombre && !proveedoresDisponibles.includes(form.proveedorNombre) && (
                    <option value={form.proveedorNombre}>{form.proveedorNombre} (histórico, sin vincular)</option>
                  )}
                  {proveedoresDisponibles.map(nombre => <option key={nombre} value={nombre}>{nombre}</option>)}
                </select>
                {errorProveedores && <p role="alert" style={{ color: 'var(--red)', fontSize: 12 }}>{errorProveedores}</p>}
              </div>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Descripción Técnica
              </label>
              <textarea
                value={form.descripcion}
                onChange={e => setForm(f => ({ ...f, descripcion: e.target.value }))}
                rows={3}
                style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
              />
            </div>

            {!saving && !puedeGuardar && motivoBloqueo && (
              <p role="status" aria-live="polite" style={{ color: 'var(--text-secondary)', fontSize: '0.8rem', margin: 0 }}>
                {motivoBloqueo}
              </p>
            )}
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setModalOpen(false)}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={!puedeGuardar}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: puedeGuardar ? 'var(--accent)' : 'var(--text-muted)',
                  color: '#fff',
                  fontWeight: 600,
                  opacity: puedeGuardar ? 1 : 0.7,
                  cursor: puedeGuardar ? 'pointer' : 'not-allowed',
                }}
              >
                {saving ? 'Guardando...' : (editingId ? 'Guardar Cambios' : 'Registrar SKU')}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Modal confirmación eliminar */}
      {confirmDeleteId && (
        <div className="modal-overlay" onClick={() => setConfirmDeleteId(null)}>
          <div className="modal" style={{ maxWidth: 400 }}>
            <div className="modal-header">
              <span className="modal-title">Confirmar Baja de Producto</span>
            </div>
            <div className="modal-body">
              <p style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', margin: 0 }}>
                ¿Está seguro de que desea dar de baja este producto del catálogo? Esta acción no puede deshacerse.
              </p>
            </div>
            <div className="modal-footer">
              <button
                className="btn btn-secondary"
                onClick={() => setConfirmDeleteId(null)}
              >
                Cancelar
              </button>
              <button
                className="btn btn-primary"
                style={{ background: 'var(--red)', border: 'none' }}
                onClick={handleConfirmDelete}
              >
                Dar de Baja
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
