import React, { useEffect, useState } from 'react';
import {
  misOrdenesVendedorApi,
  type MisOrdenesVendedorResponse,
  type OrdenVendedorDetalle,
} from '../../api/ordenes';

const TAMANO = 20;
const estados = ['Pendiente', 'Aprobada', 'EnProceso', 'Enviada', 'Entregada', 'Cancelada'];
const dinero = (importe: number) => new Intl.NumberFormat('es-SV', {
  style: 'currency', currency: 'USD',
}).format(importe);
const fecha = (value: string) => {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Fecha no disponible' : date.toLocaleString('es-SV');
};

/**
 * Consulta exclusivamente los recursos autorizados del vendedor autenticado.
 * No solicita IDs de terceros ni reutiliza /api/ordenes, exclusivo de Admin.
 */
export default function MisOrdenesVendedorPage() {
  const [pagina, setPagina] = useState(1);
  const [estado, setEstado] = useState('');
  const [revision, setRevision] = useState(0);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState('');
  const [datos, setDatos] = useState<MisOrdenesVendedorResponse | null>(null);
  const [detalleId, setDetalleId] = useState<string | null>(null);
  const [detalle, setDetalle] = useState<OrdenVendedorDetalle | null>(null);
  const [cargandoDetalle, setCargandoDetalle] = useState(false);
  const [errorDetalle, setErrorDetalle] = useState('');

  useEffect(() => {
    let vigente = true;
    setCargando(true);
    setError('');
    setDatos(null);
    setDetalleId(null);
    setDetalle(null);
    void misOrdenesVendedorApi.listar({ pagina, tamano: TAMANO, estado })
      .then(respuesta => {
        if (vigente) setDatos(respuesta);
      })
      .catch(err => {
        if (vigente) setError(err instanceof Error ? err.message : 'No se pudieron consultar las órdenes.');
      })
      .finally(() => { if (vigente) setCargando(false); });
    return () => { vigente = false; };
  }, [pagina, estado, revision]);

  useEffect(() => {
    let vigente = true;
    setDetalle(null);
    setErrorDetalle('');
    if (!detalleId) { setCargandoDetalle(false); return; }
    setCargandoDetalle(true);
    void misOrdenesVendedorApi.detalle(detalleId)
      .then(respuesta => {
        if (vigente) setDetalle(respuesta);
      })
      .catch(err => {
        if (vigente) setErrorDetalle(err instanceof Error ? err.message : 'Detalle no disponible.');
      })
      .finally(() => { if (vigente) setCargandoDetalle(false); });
    return () => { vigente = false; };
  }, [detalleId]);

  const total = datos?.total ?? 0;
  const paginas = Math.max(1, Math.ceil(total / TAMANO));

  return (
    <section aria-label="Mis órdenes" aria-busy={cargando} style={{ display: 'grid', gap: 18 }}>
      <header style={{ display: 'flex', flexWrap: 'wrap', gap: 12, justifyContent: 'space-between' }}>
        <div>
          <h1 style={{ margin: 0, fontSize: '1.6rem' }}>Mis órdenes</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>
            Consulta de pedidos vinculados a tu cuenta de vendedor. No permite modificar órdenes ni pagos.
          </p>
        </div>
        <button type="button" onClick={() => setRevision(n => n + 1)}>Actualizar</button>
      </header>

      <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
        <label htmlFor="f104-estado">Estado</label>
        <select id="f104-estado" value={estado} onChange={e => {
          setEstado(e.target.value);
          setPagina(1);
        }}>
          <option value="">Todos</option>
          {estados.map(item => <option key={item} value={item}>{item}</option>)}
        </select>
      </div>

      {error && <div role="alert">No se pudo cargar el listado: {error}</div>}
      {cargando && <p role="status">Cargando tus órdenes...</p>}
      {!cargando && !error && datos && (
        <>
          <p role="status" style={{ margin: 0 }}>
            {total} {total === 1 ? 'orden encontrada' : 'órdenes encontradas'}
          </p>
          {datos.ordenes.length === 0 ? (
            <p>No hay órdenes de tu cuenta con los filtros seleccionados.</p>
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead><tr>
                  <th scope="col">Número</th>
                  <th scope="col">Estado</th>
                  <th scope="col">Tipo</th>
                  <th scope="col">Importe</th>
                  <th scope="col">Fecha</th>
                  <th scope="col">Acción</th>
                </tr></thead>
                <tbody>
                  {datos.ordenes.map(orden => (
                    <tr key={orden.id}>
                      <td>{orden.numeroOrden}</td>
                      <td>{orden.estado}</td>
                      <td>{orden.tipoCliente}</td>
                      <td>{dinero(orden.montoTotal)}</td>
                      <td>{fecha(orden.fechaCreacion)}</td>
                      <td><button type="button" onClick={() => setDetalleId(orden.id)}>
                        Ver detalle de {orden.numeroOrden}
                      </button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <nav aria-label="Páginas de mis órdenes" style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
            <button type="button" disabled={pagina <= 1} onClick={() => setPagina(n => n - 1)}>
              Anterior
            </button>
            <span>Página {pagina} de {paginas}</span>
            <button type="button" disabled={pagina >= paginas} onClick={() => setPagina(n => n + 1)}>
              Siguiente
            </button>
          </nav>
        </>
      )}

      {detalleId && (
        <section aria-label="Detalle de mi orden" style={{
          padding: 18, border: '1px solid var(--border)', borderRadius: 8
        }}>
          <button type="button" onClick={() => setDetalleId(null)}>Cerrar detalle</button>
          {cargandoDetalle && <p role="status">Cargando detalle...</p>}
          {errorDetalle && <p role="alert">{errorDetalle}</p>}
          {detalle && (
            <>
              <h2>Orden {detalle.numeroOrden}</h2>
              <p>{detalle.estado} · {dinero(detalle.montoTotal)}</p>
              <ul>
                {detalle.items.map(item => (
                  <li key={item.productoId + item.sku}>
                    {item.sku} — {item.nombre}: {item.cantidad} × {dinero(item.precioUnitario)}
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>
      )}
    </section>
  );
}
