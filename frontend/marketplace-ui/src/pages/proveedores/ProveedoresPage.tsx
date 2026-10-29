/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useMemo, useState } from 'react';
import { Building2, Mail, Phone, RefreshCw, Search, ShoppingBag, Truck } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import AlertBanner from '../../components/ui/AlertBanner';
import DataTable, { type Column } from '../../components/ui/DataTable';
import { proveedoresApi, type ProveedorLogistico, type ProveedoresLogisticaResponse } from '../../api/proveedores';

export default function ProveedoresPage() {
  const navigate = useNavigate();
  const [data, setData] = useState<ProveedoresLogisticaResponse | null>(null);
  const [buscar, setBuscar] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const cargar = async () => {
    setLoading(true);
    setError('');
    try {
      setData(await proveedoresApi.listar());
    } catch (err) {
      setData(null);
      setError(err instanceof Error ? err.message : 'No se pudo cargar el maestro logístico de proveedores.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { cargar(); }, []);

  const items = useMemo(() => {
    const termino = buscar.trim().toLowerCase();
    if (!termino) return data?.items ?? [];
    return (data?.items ?? []).filter(p =>
      p.nombreComercial.toLowerCase().includes(termino) ||
      p.razonSocial.toLowerCase().includes(termino) ||
      p.categoriaSuministro.toLowerCase().includes(termino)
    );
  }, [data, buscar]);

  const columns: Column<ProveedorLogistico>[] = [
    {
      key: 'nombreComercial',
      header: 'Proveedor',
      render: row => (
        <div>
          <div className="td-primary">{row.nombreComercial}</div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>{row.razonSocial}</div>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{row.categoriaSuministro || 'Sin categoría de suministro'}</div>
        </div>
      ),
    },
    {
      key: 'otifHistorico',
      header: 'OTIF / Crédito',
      render: row => (
        <div>
          <strong>{row.otifHistorico.toFixed(1)}%</strong>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            {row.plazoCreditoDias > 0 ? `${row.plazoCreditoDias} días` : 'Contado'}
          </div>
        </div>
      ),
    },
    {
      key: 'productosCatalogo',
      header: 'Catálogo',
      render: row => (
        <div>
          <strong>{row.productosCatalogo}</strong> SKU
          <div style={{ fontSize: 10, color: row.productosBajoMinimo > 0 ? 'var(--red)' : 'var(--text-muted)' }}>
            {row.productosBajoMinimo} bajo mínimo
          </div>
        </div>
      ),
    },
    {
      key: 'ordenesCompraAbiertas',
      header: 'Compras abiertas',
      render: row => (
        <div>
          <strong>{row.ordenesCompraAbiertas}</strong> PO
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            {row.unidadesPendientesRecepcion} u. pendientes
          </div>
        </div>
      ),
    },
    {
      key: 'contactoPrincipal',
      header: 'Contacto',
      render: row => (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <span>{row.contactoPrincipal || 'Sin contacto'}</span>
          {row.email && <span style={{ fontSize: 10, color: 'var(--text-muted)', display: 'flex', gap: 4, alignItems: 'center' }}><Mail size={10} /> {row.email}</span>}
          {row.telefono && <span style={{ fontSize: 10, color: 'var(--text-muted)', display: 'flex', gap: 4, alignItems: 'center' }}><Phone size={10} /> {row.telefono}</span>}
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Proveedores logísticos</h1>
          <p className="page-subtitle">Vista operativa del maestro único core.proveedores</p>
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

      {error && <AlertBanner variant="danger" title="No se pudieron cargar proveedores" message={error} onClose={() => setError('')} />}

      <div className="stat-grid" style={{ marginBottom: 16 }}>
        {[
          { label: 'Proveedores activos', value: data?.total ?? 0, icon: <Building2 size={18} /> },
          { label: 'Vinculados a catálogo', value: data?.conProductosCatalogo ?? 0, icon: <Truck size={18} /> },
          { label: 'Con PO abierta', value: data?.conCompraAbierta ?? 0, icon: <ShoppingBag size={18} /> },
          { label: 'Nombres sin vincular', value: data?.nombresCatalogoNoVinculados.length ?? 0, icon: <Search size={18} /> },
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

      {(data?.nombresCatalogoNoVinculados.length ?? 0) > 0 && (
        <AlertBanner
          variant="warning"
          title="Proveedores del catálogo pendientes de vinculación"
          message={`Estos nombres no coinciden con un proveedor activo del maestro: ${data!.nombresCatalogoNoVinculados.slice(0, 8).join(', ')}${data!.nombresCatalogoNoVinculados.length > 8 ? '…' : ''}`}
        />
      )}

      <div style={{ marginBottom: 12, maxWidth: 420 }}>
        <input
          type="search"
          value={buscar}
          onChange={e => setBuscar(e.target.value)}
          placeholder="Buscar proveedor, razón social o categoría…"
          style={{ width: '100%', padding: '0.55rem 0.75rem', borderRadius: 6, border: '1px solid var(--border)', background: 'var(--bg-surface)', color: 'var(--text-primary)' }}
        />
      </div>

      <DataTable
        title="Maestro logístico de proveedores"
        columns={columns}
        data={items}
        loading={loading}
        rowKey={row => row.id}
        pageSize={10}
        emptyMessage="No hay proveedores activos en el maestro corporativo"
      />
    </div>
  );
}
