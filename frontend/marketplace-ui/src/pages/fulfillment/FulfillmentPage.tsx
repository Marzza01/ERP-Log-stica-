/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { useEffect, useState, useCallback } from 'react';
import { Truck, CheckCircle2, PackageCheck, Play, ArrowRight, Layers, Box, AlertCircle, RefreshCw } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { useToast } from '../../context/ToastContext';
import { fulfillmentApi, type ConfirmarItemPickingRequest, type EmpacarRequest } from '../../api/fulfillment';
import type { PickList, Paquete } from '../../types';
import { OPERADORES_LOGISTICOS } from '../../constants/transportistas';

export default function FulfillmentPage() {
  const [activeTab, setActiveTab] = useState<'picking' | 'packing' | 'despacho'>('picking');
  const [pickLists, setPickLists] = useState<PickList[]>([]);
  const [paquetes, setPaquetes] = useState<Paquete[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorCarga, setErrorCarga] = useState('');

  // Modal picking
  const [selectedPickList, setSelectedPickList] = useState<PickList | null>(null);
  const [confirmItems, setConfirmItems] = useState<Record<string, boolean>>({});
  const [savingPick, setSavingPick] = useState(false);

  // Modal packing
  const [packModalOpen, setPackModalOpen] = useState(false);
  const [packForm, setPackForm] = useState<EmpacarRequest>({
    ordenId: '',
    numeroOrden: '',
    pesoRealKg: 1.2,
    largoCm: 25,
    anchoCm: 20,
    altoCm: 15,
    transportista: 'LOGIMKT Express',
    departamentoDestino: 'San Salvador',
    municipioDestino: 'San Salvador',
    direccionEntrega: '',
    contactoReceptor: '',
    telefonoReceptor: '',
  });
  const [savingPack, setSavingPack] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    setErrorCarga('');
    try {
      const [pls, pkgs] = await Promise.all([
        fulfillmentApi.listarPickLists(),
        fulfillmentApi.listarPaquetes(),
      ]);
      setPickLists(pls);
      setPaquetes(pkgs);
    } catch (error) {
      setPickLists([]);
      setPaquetes([]);
      setErrorCarga(error instanceof Error ? error.message : 'No se pudo cargar Fulfillment desde la API.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const toast = useToast();

  const handleOpenPicking = (pl: PickList) => {
    setSelectedPickList(pl);
    const initialChecked: Record<string, boolean> = {};
    pl.items.forEach(it => {
      initialChecked[it.id] = (it.cantidadRecogida ?? 0) >= it.cantidadSolicitada;
    });
    setConfirmItems(initialChecked);
  };

  const toggleItemPick = (itemId: string) => {
    setConfirmItems(prev => ({ ...prev, [itemId]: !prev[itemId] }));
  };

  const handleSavePicking = async () => {
    if (!selectedPickList) return;
    setSavingPick(true);
    try {
      const confirmaciones: ConfirmarItemPickingRequest[] = selectedPickList.items.map(it => ({
        itemPickListId: it.id,
        cantidadRecogida: confirmItems[it.id] ? it.cantidadSolicitada : 0,
      }));
      await fulfillmentApi.confirmarPicking(selectedPickList.id, confirmaciones);
      toast.success('Picking Confirmado', `PickList ${selectedPickList.numeroPickList} procesada.`);
      setSelectedPickList(null);
      await loadData();
    } catch (error) {
      toast.error('Error de picking', error instanceof Error ? error.message : 'No fue posible confirmar el picking.');
    } finally {
      setSavingPick(false);
    }
  };

  const handleDespachar = async (paqueteId: string) => {
    const pkg = paquetes.find(p => p.id === paqueteId);
    try {
      const resp = await fulfillmentApi.despachar(paqueteId);
      const fechaDespacho = resp?.fechaDespacho || new Date().toISOString();

      setPaquetes(prev => prev.map(p =>
        p.id === paqueteId
          ? { ...p, estado: 'Despachado', fechaDespacho }
          : p
      ));

      toast.success(
        'Paquete Despachado',
        `Guía ${pkg?.numeroGuia || paqueteId} entregada al transportista y stock consumido.`
      );
    } catch (error) {
      const message = error instanceof Error ? error.message : 'No fue posible completar el despacho.';
      toast.error('Despacho rechazado', message);
    }
  };

  const pesoVolumetrico = ((packForm.largoCm * packForm.anchoCm * packForm.altoCm) / 5000).toFixed(2);

  const handleSavePacking = async () => {
    setSavingPack(true);
    try {
      const resp = await fulfillmentApi.empacar(packForm.ordenId, packForm);
      toast.success('Empaque Completado', `Guía ${resp.numeroGuia} generada correctamente.`);
      setPackModalOpen(false);
      await loadData();
    } catch (error) {
      toast.error('Error de empaque', error instanceof Error ? error.message : 'No fue posible completar el empaque.');
    } finally {
      setSavingPack(false);
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Fulfillment & Despacho
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Picking con ruta óptima S-Shape, estación de empaque volumétrico y despacho a última milla.
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
            onClick={() => setPackModalOpen(true)}
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
            <PackageCheck size={16} />
            Nueva Estación de Empaque
          </button>
        </div>
      </div>

      {errorCarga && (
        <AlertBanner variant="danger" title="Fulfillment no disponible" message={errorCarga} action={{ label: 'Reintentar', onClick: loadData }} />
      )}

      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', borderBottom: '1px solid var(--border)', paddingBottom: '0.5rem' }}>
        <button
          onClick={() => setActiveTab('picking')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'picking' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'picking' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'picking' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <Layers size={16} />
          Listas de Picking (S-Shape) ({pickLists.length})
        </button>

        <button
          onClick={() => setActiveTab('packing')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'packing' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'packing' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'packing' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <Box size={16} />
          Paquetes Empacados ({paquetes.filter(p => p.estado === 'ListoParaDespacho' && !p.fechaDespacho).length})
        </button>

        <button
          onClick={() => setActiveTab('despacho')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'despacho' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'despacho' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'despacho' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <Truck size={16} />
          Historial de Despachos ({paquetes.filter(p => !!p.fechaDespacho).length})
        </button>
      </div>

      {/* Tab 1: Picking */}
      {activeTab === 'picking' && (
        <DataTable
          columns={[
            {
              header: 'N° Pick List',
              accessor: 'numeroPickList',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.numeroPickList}</span>,
            },
            {
              header: 'N° Orden (OMS)',
              accessor: 'numeroOrden',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }}>{row.numeroOrden}</span>,
            },
            {
              header: 'Operador Asignado',
              accessor: 'operadorAsignado',
            },
            {
              header: 'Ítems a Recolectar',
              accessor: 'items',
              render: (row) => (
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <span>{row.items?.length ?? 0} ítems</span>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                    ({row.items?.filter((i: any) => (i.cantidadRecogida ?? 0) >= i.cantidadSolicitada).length ?? 0} listos)
                  </span>
                </div>
              ),
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: (row) => <StatusBadge status={row.estado} />,
            },
            {
              header: 'Acción',
              accessor: 'id',
              render: (row) => (
                <button
                  onClick={() => handleOpenPicking(row)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.35rem',
                    padding: '0.35rem 0.75rem',
                    borderRadius: '6px',
                    border: '1px solid var(--border)',
                    background: 'var(--bg-surface)',
                    color: 'var(--text-primary)',
                    fontSize: '0.8rem',
                    fontWeight: 600,
                    cursor: 'pointer',
                  }}
                >
                  <Play size={13} style={{ color: 'var(--green)' }} />
                  {row.estado === 'Completado' ? 'Revisar Ruta' : 'Iniciar Picking'}
                </button>
              ),
            },
          ]}
          data={pickLists}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay pick lists pendientes de recolección."
        />
      )}

      {/* Tab 2: Packing */}
      {activeTab === 'packing' && (
        <DataTable
          columns={[
            {
              header: 'Guía de Rastreo',
              accessor: 'numeroGuia',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.numeroGuia}</span>,
            },
            {
              header: 'Orden Vinculada',
              accessor: 'numeroOrden',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }}>{row.numeroOrden}</span>,
            },
            {
              header: 'Transportista',
              accessor: 'transportista',
              render: (row) => (
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                  <Truck size={14} style={{ color: 'var(--text-muted)' }} />
                  <span>{row.transportista}</span>
                </div>
              ),
            },
            {
              header: 'Peso Real',
              accessor: 'pesoRealKg',
              render: (row) => <span>{row.pesoRealKg} kg</span>,
            },
            {
              header: 'Volumen',
              accessor: 'volumenM3',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.8rem' }}>{row.volumenM3} m³</span>,
            },
            {
              header: 'Destino',
              accessor: 'municipioDestino',
              render: (row) => <span>{row.municipioDestino}, {row.departamentoDestino}</span>,
            },
            {
              header: 'Acción Despacho',
              accessor: 'id',
              render: (row) => (
                <button
                  onClick={() => handleDespachar(row.id)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.35rem',
                    padding: '0.35rem 0.75rem',
                    borderRadius: '6px',
                    border: '1px solid var(--accent)',
                    background: 'var(--accent-glow)',
                    color: 'var(--accent)',
                    fontSize: '0.8rem',
                    fontWeight: 600,
                    cursor: 'pointer',
                  }}
                >
                  <ArrowRight size={13} />
                  Despachar
                </button>
              ),
            },
          ]}
          data={paquetes.filter(p => p.estado === 'ListoParaDespacho' && !p.fechaDespacho)}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay paquetes pendientes de despacho."
        />
      )}

      {/* Tab 3: Despacho */}
      {activeTab === 'despacho' && (
        <DataTable
          columns={[
            {
              header: 'Guía de Rastreo',
              accessor: 'numeroGuia',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--green)' }}>{row.numeroGuia}</span>,
            },
            {
              header: 'Orden',
              accessor: 'numeroOrden',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }}>{row.numeroOrden}</span>,
            },
            {
              header: 'Transportista',
              accessor: 'transportista',
            },
            {
              header: 'Destino',
              accessor: 'municipioDestino',
              render: (row) => <span>{row.municipioDestino}, {row.departamentoDestino}</span>,
            },
            {
              header: 'Fecha Despacho',
              accessor: 'fechaDespacho',
              render: (row) => (
                <span style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
                  {row.fechaDespacho ? new Date(row.fechaDespacho).toLocaleString('es-SV') : 'Hoy'}
                </span>
              ),
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: () => <span style={{ color: 'var(--green)', fontWeight: 600, fontSize: '0.8rem' }}>● En Tránsito / Despachado</span>,
            },
          ]}
          data={paquetes.filter(p => p.estado === 'Despachado' || !!p.fechaDespacho)}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay despachos registrados todavía."
        />
      )}

      {/* Modal Picking S-Shape */}
      {selectedPickList && (
        <Modal
          title={`Recolección S-Shape — ${selectedPickList.numeroPickList}`}
          isOpen={true}
          onClose={() => setSelectedPickList(null)}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div style={{ background: 'var(--bg-surface)', padding: '0.75rem 1rem', borderRadius: '8px', border: '1px solid var(--border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div>
                <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Orden Asociada</div>
                <div style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>{selectedPickList.numeroOrden}</div>
              </div>
              <div>
                <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Operador Asignado</div>
                <div style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{selectedPickList.operadorAsignado}</div>
              </div>
            </div>

            <div style={{ fontSize: '0.85rem', color: 'var(--accent)', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
              <Layers size={15} />
              Ruta Óptima Ordenada: Pasillo → Rack → Nivel
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxHeight: '350px', overflowY: 'auto' }}>
              {selectedPickList.items.map((it, idx) => {
                const isChecked = !!confirmItems[it.id];
                return (
                  <div
                    key={it.id}
                    onClick={() => toggleItemPick(it.id)}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '0.75rem',
                      padding: '0.75rem',
                      borderRadius: '8px',
                      border: `1px solid ${isChecked ? 'var(--green)' : 'var(--border)'}`,
                      background: isChecked ? 'rgba(34, 197, 94, 0.05)' : 'var(--bg-card)',
                      cursor: 'pointer',
                      transition: 'all 0.15s ease',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                      {isChecked ? (
                        <CheckCircle2 size={20} style={{ color: 'var(--green)' }} />
                      ) : (
                        <div style={{ width: 18, height: 18, borderRadius: '4px', border: '2px solid var(--border-focus)' }} />
                      )}
                    </div>
                    <div style={{ flex: 1 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                        <span style={{ fontSize: '0.75rem', padding: '0.15rem 0.4rem', borderRadius: '4px', background: 'var(--bg-muted)', color: 'var(--accent)', fontWeight: 700 }}>
                          Paso {idx + 1}
                        </span>
                        <span style={{ fontWeight: 600, color: 'var(--text-primary)', fontSize: '0.9rem' }}>
                          {it.nombreProducto}
                        </span>
                      </div>
                      <div style={{ display: 'flex', gap: '1rem', marginTop: '0.3rem', fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
                        <span>SKU: <strong style={{ color: 'var(--text-primary)', fontFamily: 'var(--font-mono)' }}>{it.sku}</strong></span>
                        <span>Ubicación: <strong style={{ color: 'var(--accent)', fontFamily: 'var(--font-mono)' }}>{it.ubicacionCodigo || `P-${it.pasillo} R-${it.rack} N-${it.nivel}`}</strong></span>
                        <span>Cant: <strong style={{ color: 'var(--text-primary)' }}>{it.cantidadSolicitada}</strong></span>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setSelectedPickList(null)}
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
                onClick={handleSavePicking}
                disabled={savingPick}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingPick ? 'not-allowed' : 'pointer',
                }}
              >
                {savingPick ? 'Guardando...' : 'Confirmar Recolección'}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* Modal Nueva Estación de Empaque */}
      {packModalOpen && (
        <Modal
          title="Estación de Empaque y Cubicaje Volumétrico"
          isOpen={true}
          onClose={() => setPackModalOpen(false)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              handleSavePacking();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>ID de Orden (UUID)</label>
                <input
                  type="text"
                  placeholder="UUID de la orden"
                  value={packForm.ordenId}
                  onChange={e => setPackForm(f => ({ ...f, ordenId: e.target.value }))}
                  required
                  style={{ width: '100%', padding: '0.5rem 0.75rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>N° de Orden</label>
                <input
                  type="text"
                  placeholder="Ej. ORD-20260918-10042"
                  value={packForm.numeroOrden}
                  onChange={e => setPackForm(f => ({ ...f, numeroOrden: e.target.value }))}
                  required
                  style={{ width: '100%', padding: '0.5rem 0.75rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Transportista
                </label>
                <select
                  value={packForm.transportista}
                  onChange={e => setPackForm(f => ({ ...f, transportista: e.target.value }))}
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
                  {OPERADORES_LOGISTICOS.map(op => (
                    <option key={op.id} value={op.id}>
                      {op.nombre} (${op.tarifaEnvio.toFixed(2)} USD)
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Municipio Destino
                </label>
                <input
                  type="text"
                  value={packForm.municipioDestino}
                  onChange={e => setPackForm(f => ({ ...f, municipioDestino: e.target.value }))}
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

            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>Dirección de Entrega</label>
              <input
                type="text"
                value={packForm.direccionEntrega}
                onChange={e => setPackForm(f => ({ ...f, direccionEntrega: e.target.value }))}
                required
                style={{ width: '100%', padding: '0.5rem 0.75rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
              />
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>Contacto Receptor</label>
                <input
                  type="text"
                  value={packForm.contactoReceptor}
                  onChange={e => setPackForm(f => ({ ...f, contactoReceptor: e.target.value }))}
                  required
                  style={{ width: '100%', padding: '0.5rem 0.75rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>Teléfono Receptor</label>
                <input
                  type="text"
                  value={packForm.telefonoReceptor}
                  onChange={e => setPackForm(f => ({ ...f, telefonoReceptor: e.target.value }))}
                  required
                  style={{ width: '100%', padding: '0.5rem 0.75rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '0.5rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Peso (kg)
                </label>
                <input
                  type="number"
                  step="0.05"
                  value={packForm.pesoRealKg}
                  onChange={e => setPackForm(f => ({ ...f, pesoRealKg: parseFloat(e.target.value) || 0 }))}
                  style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Largo (cm)
                </label>
                <input
                  type="number"
                  value={packForm.largoCm}
                  onChange={e => setPackForm(f => ({ ...f, largoCm: parseInt(e.target.value) || 0 }))}
                  style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Ancho (cm)
                </label>
                <input
                  type="number"
                  value={packForm.anchoCm}
                  onChange={e => setPackForm(f => ({ ...f, anchoCm: parseInt(e.target.value) || 0 }))}
                  style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Alto (cm)
                </label>
                <input
                  type="number"
                  value={packForm.altoCm}
                  onChange={e => setPackForm(f => ({ ...f, altoCm: parseInt(e.target.value) || 0 }))}
                  style={{ width: '100%', padding: '0.4rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            {/* Cubicaje summary badge */}
            <div style={{ background: 'var(--bg-surface)', padding: '0.75rem 1rem', borderRadius: '8px', border: '1px solid var(--border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'var(--yellow)', fontSize: '0.8rem' }}>
                <AlertCircle size={16} />
                <span>Peso Volumétrico (Fórmula IATA: L×W×H / 5000):</span>
              </div>
              <div style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--text-primary)' }}>
                {pesoVolumetrico} kg
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setPackModalOpen(false)}
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
                disabled={savingPack}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingPack ? 'not-allowed' : 'pointer',
                }}
              >
                {savingPack ? 'Generando Guía...' : 'Generar Guía y Rotular'}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  );
}
