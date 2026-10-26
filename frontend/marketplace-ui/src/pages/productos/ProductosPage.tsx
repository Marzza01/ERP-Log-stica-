/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState, useCallback } from 'react';
import { ShoppingBag, Search, Plus, Filter, AlertCircle, RefreshCw, Edit2, Trash2 } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import { useToast } from '../../context/ToastContext';
import { productosApi, type CrearProductoRequest } from '../../api/productos';
import type { Producto } from '../../types';

export default function ProductosPage() {
  const [productos, setProductos] = useState<Producto[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selectedCat, setSelectedCat] = useState('');

  // Modal
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
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
  // Confirm delete
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const res = await productosApi.listar(1, 100);
      setProductos(res.items ?? []);
    } catch {
      // Fallback demo data (mismo contrato que el backend)
      const cats = ['Tecnología', 'Audio & Video', 'Periféricos Gamer', 'Cómputo', 'Accesorios'];
      const mock: Producto[] = Array.from({ length: 25 }, (_, i) => {
        const disp = (i * 7) % 65;
        const reservado = (i * 3) % 15;
        return {
          id: `00000000-0000-0000-0000-${String(i + 1).padStart(12, '0')}`,
          sku: `SKU-LOGI-${1000 + i}`,
          nombre: `Producto Demo ${i + 1}`,
          categoriaNombre: cats[i % cats.length],
          subcategoria: 'General',
          precioB2C: 25 + (i * 18.5),
          precioB2B: 20 + (i * 15),
          precioBase: 25 + (i * 18.5),
          moneda: 'USD',
          stockInicial: disp + reservado,
          stockMinimo: 10,
          proveedorNombre: ['Samsung', 'Sony', 'ASUS', 'Logitech', 'Anker'][i % 5],
          paisOrigen: 'El Salvador',
          pesoKg: 0.5,
          dimensionesCm: '20x15x5',
          unidadMedida: 'Unidad',
          estadoProducto: 'Activo',
          codigoEAN: '',
          descripcion: 'Producto de demostración para desarrollo local.',
          vendedorId: '11111111-1111-1111-1111-111111111111',
          categoriaId: '22222222-2222-2222-2222-222222222222',
          estaPublicado: true,
          fechaCreacion: new Date().toISOString(),
          stock: {
            id: `10000000-0000-0000-0000-${String(i + 1).padStart(12, '0')}`,
            productoId: `00000000-0000-0000-0000-${String(i + 1).padStart(12, '0')}`,
            cantidadDisponible: disp,
            cantidadReservada: reservado,
            cantidadTotal: disp + reservado,
            puntoReorden: 10,
            esBajoMinimo: disp <= 10,
            ubicacionCodigo: 'PAS-A-RCK-01-NIV-01-POS-01',
            ultimaActualizacion: new Date().toISOString(),
          },
        };
      });
      setProductos(mock);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const filtered = productos.filter(p => {
    const matchSearch = p.nombre.toLowerCase().includes(search.toLowerCase()) ||
                        p.sku.toLowerCase().includes(search.toLowerCase());
    const matchCat = selectedCat ? p.categoriaNombre === selectedCat : true;
    return matchSearch && matchCat;
  });

  const totalDisponibles = productos.reduce((acc, p) => acc + (p.stock?.cantidadDisponible ?? 0), 0);
  const totalReservados = productos.reduce((acc, p) => acc + (p.stock?.cantidadReservada ?? 0), 0);
  const skusCriticos = productos.filter(p => (p.stock?.cantidadDisponible ?? 0) < 10).length;

  const handleOpenCrear = () => {
    setEditingId(null);
    setForm({
      sku: `SKU-LOGI-${Math.floor(1000 + Math.random() * 9000)}`,
      nombre: '',
      descripcion: '',
      categoriaNombre: 'Tecnología',
      subcategoria: 'General',
      precioB2C: 49.99,
      precioB2B: 44.99,
      stockMinimo: 10,
      proveedorNombre: '',
      paisOrigen: 'El Salvador',
      pesoKg: 0.5,
      dimensionesCm: '20x15x5',
      unidadMedida: 'Unidad',
      estadoProducto: 'Activo',
      estaPublicado: true,
    });
    setModalOpen(true);
  };

  const handleOpenEditar = (p: Producto) => {
    setEditingId(p.id);
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

  const handleSave = async () => {
    setSaving(true);
    try {
      if (editingId) {
        await productosApi.actualizar(editingId, form);
        toast.success('Producto Actualizado', `El SKU ${form.sku} se actualizó correctamente.`);
      } else {
        await productosApi.crear(form);
        toast.success('Producto Creado', `El SKU ${form.sku} se agregó al catálogo comercial.`);
      }
    } catch {
      // optimistic fallback
      if (editingId) {
        toast.info('Producto Actualizado (Local)', `Cambios reflejados localmente.`);
      } else {
        toast.success('Producto Creado (Local)', `El SKU ${form.sku} se agregó a la vista.`);
      }
    }
    if (editingId) {
      setProductos(prev => prev.map(p => p.id === editingId ? {
        ...p,
        ...form,
        precioBase: form.precioB2C,
      } : p));
    } else {
      // Recargar desde backend para conservar IDs y valores asignados por el servidor.
      try {
        const res = await productosApi.listar(1, 100);
        setProductos(res.items ?? []);
      } catch {
        // El fallback visual se mantiene por ahora; se retirará en la fase de eliminación de mocks.
      }
    }
    setSaving(false);
    setModalOpen(false);
  };

  const handleDelete = async (id: string) => {
    setConfirmDeleteId(id);
  };

  const handleConfirmDelete = async () => {
    if (!confirmDeleteId) return;
    try {
      await productosApi.eliminar(confirmDeleteId);
      toast.warning('Producto Dado de Baja', 'El producto ha sido retirado del catálogo.');
    } catch {
      toast.warning('Producto Dado de Baja (Local)', 'El producto ha sido retirado de la vista.');
    }
    setProductos(prev => prev.filter(p => p.id !== confirmDeleteId));
    setConfirmDeleteId(null);
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
            Control de catálogo maestro de 500 SKUs, especificaciones logísticas (peso/volumen) y disponibilidad en bodega.
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

      {/* KPI Cards Strip */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1rem' }}>
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total SKUs Registrados</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem' }}>
            {productos.length}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--accent)', marginTop: '0.25rem' }}>Catálogo Activo</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Stock Físico Disponible</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--green)', marginTop: '0.35rem' }}>
            {totalDisponibles.toLocaleString()} unid.
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Listo para picking en WMS</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Stock Reservado (OMS)</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--yellow)', marginTop: '0.35rem' }}>
            {totalReservados.toLocaleString()} unid.
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>En órdenes activas</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Alertas de Reorden</div>
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
            onChange={e => setSearch(e.target.value)}
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

        <div style={{ width: '220px' }}>
          <select
            value={selectedCat}
            onChange={e => setSelectedCat(e.target.value)}
            style={{
              width: '100%',
              padding: '0.55rem 0.75rem',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              fontSize: '0.875rem',
              boxSizing: 'border-box',
            }}
          >
            <option value="">Todas las Categorías</option>
            {Array.from(new Set(productos.map(p => p.categoriaNombre))).filter(Boolean).sort().map(cat => (
              <option key={cat} value={cat}>{cat}</option>
            ))}
          </select>
        </div>
      </div>

      {/* Tabla Productos */}
      <DataTable
        columns={[
          {
            header: 'SKU',
            accessor: 'sku',
            render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.sku}</span>,
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
        data={filtered}
        keyExtractor={(item) => item.id}
        loading={loading}
        emptyText="No se encontraron productos coincidentes."
      />

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
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  SKU
                </label>
                <input
                  type="text"
                  value={form.sku}
                  onChange={e => setForm(f => ({ ...f, sku: e.target.value }))}
                  required
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', fontFamily: 'var(--font-mono)', boxSizing: 'border-box' }}
                />
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
                <select
                  value={form.categoriaNombre}
                  onChange={e => setForm(f => ({ ...f, categoriaNombre: e.target.value }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                >
                  <option value="Tecnología">Tecnología</option>
                  <option value="Audio & Video">Audio & Video</option>
                  <option value="Periféricos Gamer">Periféricos Gamer</option>
                  <option value="Cómputo">Cómputo</option>
                  <option value="Accesorios">Accesorios</option>
                </select>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Proveedor
                </label>
                <input
                  type="text"
                  value={form.proveedorNombre}
                  onChange={e => setForm(f => ({ ...f, proveedorNombre: e.target.value }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
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
                disabled={saving}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: saving ? 'not-allowed' : 'pointer',
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
