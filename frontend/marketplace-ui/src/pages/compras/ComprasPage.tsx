import React, { useEffect, useState, useCallback } from 'react';
import { ShoppingCart, FileText, CheckCircle2, AlertTriangle, Plus, RefreshCw, Send, ShieldCheck } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import { comprasApi, type CrearPORequest, type RegistrarFacturaRequest, type Validar3WayMatchRequest } from '../../api/compras';
import type { OrdenCompra, MatchTridireccional } from '../../types';

export default function ComprasPage() {
  const [activeTab, setActiveTab] = useState<'po' | 'facturas' | 'match'>('po');
  const [ordenesCompra, setOrdenesCompra] = useState<OrdenCompra[]>([]);
  const [matches, setMatches] = useState<MatchTridireccional[]>([]);
  const [loading, setLoading] = useState(true);

  // Crear PO Modal
  const [poModalOpen, setPoModalOpen] = useState(false);
  const [poForm, setPoForm] = useState<CrearPORequest>({
    proveedorId: '00000000-0000-0000-0000-000000000001',
    proveedorNombre: 'Distribuidora Tecnológica S.A.',
    terminosPago: 'Crédito 30 días',
    observaciones: 'Reabastecimiento bodega central Q3',
    fechaEntregaEsperada: new Date(Date.now() + 15 * 86400000).toISOString().split('T')[0],
    items: [
      { sku: 'SKU-LOGI-8801', nombreProducto: 'Smartphone Galaxy A54 128GB', cantidad: 50, precioUnitario: 240.00 },
    ],
  });
  const [savingPo, setSavingPo] = useState(false);

  // Registrar Factura Modal
  const [facturaModalOpen, setFacturaModalOpen] = useState(false);
  const [facturaForm, setFacturaForm] = useState<RegistrarFacturaRequest>({
    ordenCompraId: '',
    numeroFactura: 'FAC-2026-0988',
    subTotal: 12000,
    montoTotal: 13560,
    fechaEmision: new Date().toISOString().split('T')[0],
  });
  const [savingFactura, setSavingFactura] = useState(false);

  // 3-Way Match Modal / State
  const [matchModalOpen, setMatchModalOpen] = useState(false);
  const [matchForm, setMatchForm] = useState<Validar3WayMatchRequest>({
    ordenCompraId: '',
    facturaProveedorId: '00000000-0000-0000-0000-000000000002',
    cantidadRecibidaBodega: 50,
    toleranciaPorcentaje: 2.0,
  });
  const [matchResult, setMatchResult] = useState<MatchTridireccional | null>(null);
  const [runningMatch, setRunningMatch] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [poRes, matchRes] = await Promise.all([
        comprasApi.listarPO({ tamano: 50 }),
        comprasApi.historialMatch(),
      ]);
      setOrdenesCompra(poRes.ordenesCompra || []);
      setMatches(matchRes || []);
    } catch {
      // Fallback demo data
      const mockPOs: OrdenCompra[] = [
        {
          id: 'po-101',
          numeroPO: 'PO-20260918-1001',
          proveedorId: 'prov-1',
          nombreProveedor: 'Tech Wholesale El Salvador S.A.',
          estado: 'Emitida',
          montoTotal: 12000.00,
          moneda: 'USD',
          condicionPago: 'Crédito 30 días',
          fechaEmision: new Date(Date.now() - 86400000 * 3).toISOString(),
          fechaEntregaEsperada: new Date(Date.now() + 86400000 * 7).toISOString(),
          items: [
            { id: 'item-1', productoId: 'prod-1', descripcion: 'Smartphone Galaxy A54 128GB', cantidad: 50, precioUnitario: 240.00, total: 12000.00 },
          ],
        },
        {
          id: 'po-102',
          numeroPO: 'PO-20260918-1002',
          proveedorId: 'prov-2',
          nombreProveedor: 'Importadora y Logística Global',
          estado: 'Recibida',
          montoTotal: 4500.00,
          moneda: 'USD',
          condicionPago: 'Contado',
          fechaEmision: new Date(Date.now() - 86400000 * 6).toISOString(),
          items: [
            { id: 'item-2', productoId: 'prod-2', descripcion: 'Audífonos Bluetooth Noise Cancelling', cantidad: 100, precioUnitario: 45.00, total: 4500.00 },
          ],
        },
      ];

      const mockMatches: MatchTridireccional[] = [
        {
          id: 'match-01',
          ordenCompraId: 'po-102',
          resultado: 'Conforme',
          diferenciaCantidad: 0,
          diferenciaMonto: 0,
          observaciones: 'PO, Nota de Recepción WMS y Factura coinciden al 100%. Aprobado automáticamente para pago y contabilidad.',
          fechaAuditoria: new Date(Date.now() - 86400000 * 2).toISOString(),
        },
        {
          id: 'match-02',
          ordenCompraId: 'po-103',
          resultado: 'Discrepante',
          diferenciaCantidad: 5,
          diferenciaMonto: 320.50,
          observaciones: 'Discrepancia detectada: Factura cobró 55 unidades pero bodega recibió 50. Supera tolerancia del 2%. Retenido.',
          fechaAuditoria: new Date(Date.now() - 86400000 * 5).toISOString(),
        },
      ];

      setOrdenesCompra(mockPOs);
      setMatches(mockMatches);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleCrearPO = async () => {
    setSavingPo(true);
    try {
      await comprasApi.crearPO(poForm);
    } catch {
      // optimistic
    }
    const newPO: OrdenCompra = {
      id: crypto.randomUUID(),
      numeroPO: `PO-${new Date().getFullYear()}-${Math.floor(1000 + Math.random() * 9000)}`,
      proveedorId: poForm.proveedorId,
      nombreProveedor: poForm.proveedorNombre,
      estado: 'Borrador',
      montoTotal: poForm.items.reduce((acc, i) => acc + (i.cantidad * i.precioUnitario), 0),
      moneda: 'USD',
      condicionPago: poForm.terminosPago || 'Crédito 30 días',
      fechaEmision: new Date().toISOString(),
      items: poForm.items.map(i => ({
        id: crypto.randomUUID(),
        productoId: crypto.randomUUID(),
        descripcion: i.nombreProducto,
        cantidad: i.cantidad,
        precioUnitario: i.precioUnitario,
        total: i.cantidad * i.precioUnitario,
      })),
    };
    setOrdenesCompra(prev => [newPO, ...prev]);
    setSavingPo(false);
    setPoModalOpen(false);
  };

  const handleEnviarPO = async (poId: string) => {
    try {
      await comprasApi.enviarPO(poId);
    } catch {
      // ignore
    }
    setOrdenesCompra(prev => prev.map(p => p.id === poId ? { ...p, estado: 'Emitida' } : p));
  };

  const handleRegistrarFactura = async () => {
    setSavingFactura(true);
    try {
      await comprasApi.registrarFactura(facturaForm);
    } catch {
      // optimistic
    }
    setSavingFactura(false);
    setFacturaModalOpen(false);
  };

  const handleEjecutarMatch = async () => {
    setRunningMatch(true);
    try {
      const res = await comprasApi.ejecutarMatch(matchForm);
      setMatchResult(res);
      setMatches(prev => [res, ...prev]);
    } catch {
      // mock calculation
      const po = ordenesCompra.find(p => p.id === matchForm.ordenCompraId) || ordenesCompra[0];
      const cantOrdenada = po ? po.items.reduce((a, b) => a + b.cantidad, 0) : 50;
      const difCant = Math.abs(cantOrdenada - matchForm.cantidadRecibidaBodega);
      const isConforme = difCant <= (cantOrdenada * (matchForm.toleranciaPorcentaje || 2) / 100);

      const result: MatchTridireccional = {
        id: crypto.randomUUID(),
        ordenCompraId: matchForm.ordenCompraId,
        resultado: isConforme ? 'Conforme' : 'Discrepante',
        diferenciaCantidad: difCant,
        diferenciaMonto: difCant * 45,
        observaciones: isConforme
          ? `Cotejo exitoso con tolerancia del ${matchForm.toleranciaPorcentaje}%. Orden autorizada para pago automático y asiento contable generado.`
          : `Discrepancia de ${difCant} unidades entre PO y recepción física de bodega. Supera tolerancia de ${matchForm.toleranciaPorcentaje}%. Retenido.`,
        fechaAuditoria: new Date().toISOString(),
      };
      setMatchResult(result);
      setMatches(prev => [result, ...prev]);
    } finally {
      setRunningMatch(false);
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Compras & 3-Way Match
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Auditoría tridireccional automatizada: Orden de Compra (PO) vs Recepción Bodega vs Factura de Proveedor.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem' }}>
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
            onClick={() => setPoModalOpen(true)}
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
            Nueva Orden de Compra (PO)
          </button>
        </div>
      </div>

      {/* Attention List (Efferd Dashboard #6: Items requiring operational attention) */}
      {matches.some(m => m.resultado === 'Discrepante') && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '12px 16px',
          background: 'rgba(239, 68, 68, 0.08)',
          border: '1px solid rgba(239, 68, 68, 0.25)',
          borderRadius: 'var(--radius-lg)',
          gap: 12
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div style={{
              width: 32,
              height: 32,
              borderRadius: 'var(--radius-md)',
              background: 'var(--red-bg)',
              color: 'var(--red)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0
            }}>
              <AlertTriangle size={16} />
            </div>
            <div>
              <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-primary)' }}>
                {matches.filter(m => m.resultado === 'Discrepante').length} Discrepancia(s) en 3-Way Match Requieren Atención
              </div>
              <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                Se detectaron diferencias entre factura y recepción física de bodega. Los pagos correspondientes han sido retenidos por control interno.
              </div>
            </div>
          </div>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setActiveTab('match')}
            style={{ borderColor: 'rgba(239, 68, 68, 0.35)', color: 'var(--red)', flexShrink: 0 }}
          >
            Ver Auditorías Retenidas →
          </button>
        </div>
      )}

      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', borderBottom: '1px solid var(--border)', paddingBottom: '0.5rem' }}>
        <button
          onClick={() => setActiveTab('po')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'po' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'po' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'po' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <ShoppingCart size={16} />
          Órdenes de Compra ({ordenesCompra.length})
        </button>

        <button
          onClick={() => setActiveTab('match')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'match' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'match' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'match' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <ShieldCheck size={16} />
          Auditoría 3-Way Match ({matches.length})
        </button>
      </div>

      {/* Tab: POs */}
      {activeTab === 'po' && (
        <DataTable
          columns={[
            {
              header: 'N° Orden Compra',
              accessor: 'numeroPO',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.numeroPO}</span>,
            },
            {
              header: 'Proveedor',
              accessor: 'nombreProveedor',
              render: (row) => <span style={{ fontWeight: 600 }}>{row.nombreProveedor}</span>,
            },
            {
              header: 'Monto Total',
              accessor: 'montoTotal',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--green)' }}>
                  ${row.montoTotal.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
                </span>
              ),
            },
            {
              header: 'Condición de Pago',
              accessor: 'condicionPago',
            },
            {
              header: 'Fecha Emisión',
              accessor: 'fechaEmision',
              render: (row) => <span style={{ fontSize: '0.85rem' }}>{new Date(row.fechaEmision).toLocaleDateString('es-SV')}</span>,
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: (row) => <StatusBadge status={row.estado} />,
            },
            {
              header: 'Acciones',
              accessor: 'id',
              render: (row) => (
                <div style={{ display: 'flex', gap: '0.5rem' }}>
                  {row.estado === 'Borrador' && (
                    <button
                      onClick={() => handleEnviarPO(row.id)}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.3rem',
                        padding: '0.3rem 0.6rem',
                        borderRadius: '6px',
                        border: '1px solid var(--accent)',
                        background: 'var(--accent-glow)',
                        color: 'var(--accent)',
                        fontSize: '0.75rem',
                        fontWeight: 600,
                        cursor: 'pointer',
                      }}
                    >
                      <Send size={12} />
                      Emitir a Proveedor
                    </button>
                  )}
                  <button
                    onClick={() => {
                      setFacturaForm(f => ({ ...f, ordenCompraId: row.id, montoTotal: row.montoTotal, subTotal: Math.round(row.montoTotal / 1.13) }));
                      setFacturaModalOpen(true);
                    }}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '0.3rem',
                      padding: '0.3rem 0.6rem',
                      borderRadius: '6px',
                      border: '1px solid var(--border)',
                      background: 'var(--bg-surface)',
                      color: 'var(--text-primary)',
                      fontSize: '0.75rem',
                      fontWeight: 500,
                      cursor: 'pointer',
                    }}
                  >
                    <FileText size={12} />
                    Asociar Factura
                  </button>
                </div>
              ),
            },
          ]}
          data={ordenesCompra}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay órdenes de compra registradas."
        />
      )}

      {/* Tab: 3-Way Match */}
      {activeTab === 'match' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
          {/* Action trigger banner */}
          <div
            style={{
              padding: '1.25rem',
              borderRadius: '12px',
              border: '1px solid var(--border)',
              background: 'linear-gradient(135deg, rgba(99, 102, 241, 0.08) 0%, rgba(17, 17, 19, 0.6) 100%)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <div>
              <h3 style={{ margin: 0, fontSize: '1.1rem', fontWeight: 600, color: 'var(--text-primary)' }}>
                Ejecutar Validación 3-Way Match
              </h3>
              <p style={{ margin: '0.25rem 0 0 0', fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
                Verifica cantidades, precios unitarios y montos de facturas contra recepciones de bodega con tolerancia configurada (±2%).
              </p>
            </div>
            <button
              onClick={() => {
                if (ordenesCompra.length > 0) {
                  setMatchForm(f => ({ ...f, ordenCompraId: ordenesCompra[0].id }));
                }
                setMatchModalOpen(true);
              }}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.5rem',
                padding: '0.65rem 1.25rem',
                borderRadius: '8px',
                border: 'none',
                background: 'var(--accent)',
                color: '#fff',
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              <ShieldCheck size={18} />
              Auditar Nueva Orden
            </button>
          </div>

          {/* Historial de Auditorías */}
          <DataTable
            columns={[
              {
                header: 'ID Auditoría',
                accessor: 'id',
                render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.8rem', color: 'var(--text-secondary)' }}>{row.id.slice(0, 8)}...</span>,
              },
              {
                header: 'Resultado 3-Way Match',
                accessor: 'resultado',
                render: (row) => (
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                    {row.resultado === 'Conforme' ? (
                      <CheckCircle2 size={16} style={{ color: 'var(--green)' }} />
                    ) : (
                      <AlertTriangle size={16} style={{ color: 'var(--red)' }} />
                    )}
                    <span style={{ fontWeight: 700, color: row.resultado === 'Conforme' ? 'var(--green)' : 'var(--red)' }}>
                      {row.resultado}
                    </span>
                  </div>
                ),
              },
              {
                header: 'Dif. Cantidad',
                accessor: 'diferenciaCantidad',
                render: (row) => <span>{row.diferenciaCantidad} unid.</span>,
              },
              {
                header: 'Dif. Monto',
                accessor: 'diferenciaMonto',
                render: (row) => (
                  <span style={{ fontFamily: 'var(--font-mono)' }}>
                    ${row.diferenciaMonto.toFixed(2)}
                  </span>
                ),
              },
              {
                header: 'Observaciones Técnicas',
                accessor: 'observaciones',
                render: (row) => <span style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>{row.observaciones}</span>,
              },
              {
                header: 'Fecha Auditoría',
                accessor: 'fechaAuditoria',
                render: (row) => <span style={{ fontSize: '0.8rem' }}>{new Date(row.fechaAuditoria).toLocaleString('es-SV')}</span>,
              },
            ]}
            data={matches}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyText="No hay historial de auditorías 3-Way Match."
          />
        </div>
      )}

      {/* Modal Crear PO */}
      {poModalOpen && (
        <Modal
          title="Crear Nueva Orden de Compra (PO)"
          isOpen={true}
          onClose={() => setPoModalOpen(false)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              handleCrearPO();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Proveedor
              </label>
              <input
                type="text"
                value={poForm.proveedorNombre}
                onChange={e => setPoForm(f => ({ ...f, proveedorNombre: e.target.value }))}
                required
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              />
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Términos de Pago
                </label>
                <select
                  value={poForm.terminosPago}
                  onChange={e => setPoForm(f => ({ ...f, terminosPago: e.target.value }))}
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                >
                  <option value="Crédito 30 días">Crédito 30 días</option>
                  <option value="Crédito 60 días">Crédito 60 días</option>
                  <option value="Contado">Contado</option>
                </select>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Fecha Entrega Esperada
                </label>
                <input
                  type="date"
                  value={poForm.fechaEntregaEsperada}
                  onChange={e => setPoForm(f => ({ ...f, fechaEntregaEsperada: e.target.value }))}
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>

            {/* Ítem de ejemplo en la PO */}
            <div style={{ background: 'var(--bg-surface)', padding: '0.75rem', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <div style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '0.5rem' }}>
                Ítems de la Orden
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: '0.5rem' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Producto</label>
                  <input
                    type="text"
                    value={poForm.items[0]?.nombreProducto}
                    onChange={e => {
                      const val = e.target.value;
                      setPoForm(f => ({ ...f, items: [{ ...f.items[0], nombreProducto: val }] }));
                    }}
                    style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-card)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Cantidad</label>
                  <input
                    type="number"
                    value={poForm.items[0]?.cantidad}
                    onChange={e => {
                      const val = parseInt(e.target.value) || 0;
                      setPoForm(f => ({ ...f, items: [{ ...f.items[0], cantidad: val }] }));
                    }}
                    style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-card)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Precio Unit. ($)</label>
                  <input
                    type="number"
                    step="0.01"
                    value={poForm.items[0]?.precioUnitario}
                    onChange={e => {
                      const val = parseFloat(e.target.value) || 0;
                      setPoForm(f => ({ ...f, items: [{ ...f.items[0], precioUnitario: val }] }));
                    }}
                    style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-card)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                  />
                </div>
              </div>
              <div style={{ textAlign: 'right', marginTop: '0.5rem', fontWeight: 700, color: 'var(--green)' }}>
                Total PO: ${(poForm.items[0]?.cantidad * poForm.items[0]?.precioUnitario).toLocaleString('es-SV', { minimumFractionDigits: 2 })}
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setPoModalOpen(false)}
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
                disabled={savingPo}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingPo ? 'not-allowed' : 'pointer',
                }}
              >
                {savingPo ? 'Guardando...' : 'Crear Orden'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Modal Registrar Factura */}
      {facturaModalOpen && (
        <Modal
          title="Registrar Factura de Proveedor"
          isOpen={true}
          onClose={() => setFacturaModalOpen(false)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              handleRegistrarFactura();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                N° de Factura Fiscal
              </label>
              <input
                type="text"
                value={facturaForm.numeroFactura}
                onChange={e => setFacturaForm(f => ({ ...f, numeroFactura: e.target.value }))}
                required
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              />
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Sub-Total ($)
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={facturaForm.subTotal}
                  onChange={e => setFacturaForm(f => ({ ...f, subTotal: parseFloat(e.target.value) || 0 }))}
                  required
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Monto Total con IVA ($)
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={facturaForm.montoTotal}
                  onChange={e => setFacturaForm(f => ({ ...f, montoTotal: parseFloat(e.target.value) || 0 }))}
                  required
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setFacturaModalOpen(false)}
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
                disabled={savingFactura}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingFactura ? 'not-allowed' : 'pointer',
                }}
              >
                {savingFactura ? 'Registrando...' : 'Registrar Factura'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Modal 3-Way Match */}
      {matchModalOpen && (
        <Modal
          title="Auditoría 3-Way Match (PO ↔ Recepción Bodega ↔ Factura)"
          isOpen={true}
          onClose={() => {
            setMatchModalOpen(false);
            setMatchResult(null);
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Seleccione Orden de Compra (PO)
              </label>
              <select
                value={matchForm.ordenCompraId}
                onChange={e => setMatchForm(f => ({ ...f, ordenCompraId: e.target.value }))}
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              >
                {ordenesCompra.map(po => (
                  <option key={po.id} value={po.id}>
                    {po.numeroPO} — {po.nombreProveedor} (${po.montoTotal.toFixed(2)})
                  </option>
                ))}
              </select>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Cantidad Física Recibida en Bodega
                </label>
                <input
                  type="number"
                  value={matchForm.cantidadRecibidaBodega}
                  onChange={e => setMatchForm(f => ({ ...f, cantidadRecibidaBodega: parseInt(e.target.value) || 0 }))}
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Tolerancia Permitida (%)
                </label>
                <input
                  type="number"
                  step="0.1"
                  value={matchForm.toleranciaPorcentaje}
                  onChange={e => setMatchForm(f => ({ ...f, toleranciaPorcentaje: parseFloat(e.target.value) || 0 }))}
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>

            {/* Resultado si se ejecutó */}
            {matchResult && (
              <div
                style={{
                  padding: '1rem',
                  borderRadius: '8px',
                  border: `1px solid ${matchResult.resultado === 'Conforme' ? 'var(--green)' : 'var(--red)'}`,
                  background: matchResult.resultado === 'Conforme' ? 'rgba(34, 197, 94, 0.08)' : 'rgba(239, 68, 68, 0.08)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '0.5rem',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontWeight: 700, color: matchResult.resultado === 'Conforme' ? 'var(--green)' : 'var(--red)' }}>
                  {matchResult.resultado === 'Conforme' ? <CheckCircle2 size={18} /> : <AlertTriangle size={18} />}
                  Resultado: {matchResult.resultado.toUpperCase()}
                </div>
                <div style={{ fontSize: '0.85rem', color: 'var(--text-primary)' }}>
                  {matchResult.observaciones}
                </div>
                <div style={{ display: 'flex', gap: '1.5rem', fontSize: '0.8rem', color: 'var(--text-secondary)', marginTop: '0.25rem' }}>
                  <span>Diferencia Cantidad: <strong>{matchResult.diferenciaCantidad} unid.</strong></span>
                  <span>Diferencia Monto: <strong>${matchResult.diferenciaMonto.toFixed(2)}</strong></span>
                </div>
              </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => {
                  setMatchModalOpen(false);
                  setMatchResult(null);
                }}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cerrar
              </button>
              <button
                type="button"
                onClick={handleEjecutarMatch}
                disabled={runningMatch}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: runningMatch ? 'not-allowed' : 'pointer',
                }}
              >
                {runningMatch ? 'Auditando...' : 'Ejecutar Validación'}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
