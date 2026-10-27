/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState } from 'react';
import { AlertTriangle, PackageCheck, RefreshCw, ShoppingBag, Warehouse } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import DataTable, { type Column } from '../../components/ui/DataTable';
import AlertBanner from '../../components/ui/AlertBanner';
import { reabastecimientoApi, type NecesidadReabastecimiento, type ReabastecimientoResponse } from '../../api/reabastecimiento';

export default function ReabastecimientoPage() {
  const navigate = useNavigate();
  const [data, setData] = useState<ReabastecimientoResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const cargar = async () => {
    setLoading(true);
    setError('');
    try {
      setData(await reabastecimientoApi.necesidades());
    } catch (err) {
      setData(null);
      setError(err instanceof Error ? err.message : 'No se pudieron cargar las necesidades de reabastecimiento.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { cargar(); }, []);

  const columns: Column<NecesidadReabastecimiento>[] = [
    {
      key: 'sku',
      header: 'SKU / Producto',
      render: row => (
        <div>
          <div className="td-mono td-primary">{row.sku}</div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>{row.nombre}</div>
        </div>
      ),
    },
    {
      key: 'cantidadDisponible',
      header: 'Disponible / Mínimo',
      render: row => (
        <div>
          <strong>{row.cantidadDisponible}</strong> / {row.stockMinimo}
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            Físico {row.cantidadFisica} · Reservado {row.cantidadReservada} · Bloqueado {row.cantidadBloqueada}
          </div>
        </div>
      ),
    },
    {
      key: 'nivelCriticidad',
      header: 'Nivel',
      render: row => (
        <span className={`badge ${row.nivelCriticidad === 'AGOTADO' ? 'badge-cancelada' : row.nivelCriticidad === 'BAJO_MINIMO' ? 'badge-pendiente' : 'badge-default'}`}>
          {row.nivelCriticidad === 'AGOTADO' ? 'Agotado' : row.nivelCriticidad === 'BAJO_MINIMO' ? 'Bajo mínimo' : 'Punto de reorden'}
        </span>
      ),
    },
    {
      key: 'proveedorNombre',
      header: 'Proveedor catálogo',
      render: row => row.proveedorNombre || 'Sin proveedor',
    },
    {
      key: 'cantidadEnCompra',
      header: 'Compra en curso',
      render: row => (
        <div>
          <strong>{row.cantidadEnCompra}</strong> u.
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            {row.ordenesCompra.length ? row.ordenesCompra.join(', ') : 'Sin PO abierta'}
          </div>
        </div>
      ),
    },
    {
      key: 'disponibleProyectado',
      header: 'Proyección',
      render: row => (
        <div>
          {row.disponibleProyectado} u.
          <div style={{ fontSize: 10, color: row.faltanteProyectado > 0 ? 'var(--red)' : 'var(--green)' }}>
            {row.faltanteProyectado > 0
              ? `Aún faltan ${row.faltanteProyectado} para salir de alerta`
              : 'Cobertura suficiente con PO actual'}
          </div>
        </div>
      ),
    },
    {
      key: 'estadoCompra',
      header: 'Atención',
      render: row => (
        <span className={`badge ${row.estadoCompra === 'CUBIERTA_POR_PO' ? 'badge-entregada' : row.estadoCompra === 'COBERTURA_PARCIAL' ? 'badge-pendiente' : 'badge-cancelada'}`}>
          {row.estadoCompra === 'CUBIERTA_POR_PO' ? 'Cubierta por PO' : row.estadoCompra === 'COBERTURA_PARCIAL' ? 'Cobertura parcial' : 'Requiere compra'}
        </span>
      ),
    },
  ];

  const items = data?.items ?? [];

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Reabastecimiento operativo</h1>
          <p className="page-subtitle">Necesidades calculadas desde stock real y órdenes de compra abiertas</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-secondary" onClick={() => navigate('/compras')}>
            <ShoppingBag size={13} /> Ir a Compras
          </button>
          <button className="btn btn-secondary" onClick={cargar} disabled={loading}>
            <RefreshCw size={13} /> Actualizar
          </button>
        </div>
      </div>

      {error && (
        <AlertBanner
          variant="danger"
          title="No se pudo consultar reabastecimiento"
          message={error}
          onClose={() => setError('')}
        />
      )}

      <div className="stat-grid" style={{ marginBottom: 16 }}>
        {[
          { label: 'Necesidades', value: data?.total ?? 0, icon: <Warehouse size={18} /> },
          { label: 'Agotados', value: data?.agotados ?? 0, icon: <AlertTriangle size={18} /> },
          { label: 'Con PO en curso', value: data?.conCompraEnCurso ?? 0, icon: <ShoppingBag size={18} /> },
          { label: 'Sin cobertura completa', value: data?.sinCoberturaCompleta ?? 0, icon: <PackageCheck size={18} /> },
        ].map(card => (
          <div key={card.label} className="stat-card">
            <div className="stat-icon-wrap">{card.icon}</div>
            <div className="stat-info">
              <div className="stat-header"><span className="stat-label">{card.label}</span></div>
              <div className="stat-value">{card.value}</div>
            </div>
          </div>
        ))}
      </div>

      <AlertBanner
        variant="info"
        title="Límite de responsabilidad"
        message="Este módulo detecta la necesidad logística y considera las PO abiertas. La creación y aprobación formal de órdenes de compra permanece en Compras. La vinculación con el maestro de proveedores se completa en la Fase 5D."
      />

      <DataTable
        title="Necesidades de reabastecimiento"
        columns={columns}
        data={items}
        loading={loading}
        rowKey={row => row.productoId}
        pageSize={10}
        emptyMessage="No hay productos en o por debajo del stock mínimo"
      />
    </div>
  );
}
