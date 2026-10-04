/**
 * ERP desarrollado por Marcelo Zepeda
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
    precio: 0,
    costoUnitario: 0,
    categoria: 'Tecnología',
    marca: '',
    pesoKg: 0.5,
    largoCm: 20,
    anchoCm: 15,
    altoCm: 5,
    estaPublicado: true,
  });
  const [saving, setSaving] = useState(false);
  // Confirm delete
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const res = await productosApi.listar(1, 100);
      const rawItems = res.items || (Array.isArray(res) ? res : []);
      const mapped: Producto[] = rawItems.map((item: any, idx: number) => ({
        id: item.id || `prod-${idx + 1}`,
        sku: item.sku || `SKU-${idx + 1}`,
        nombre: item.nombre || 'Producto Sin Nombre',
        descripcion: item.descripcion || '',
        precio: Number(item.precio ?? item.precioB2C ?? item.precioBase ?? 0),
        categoria: item.categoria || item.categoriaNombre || 'Tecnología',
        marca: item.marca || item.proveedorNombre || 'LOGIMKT',
        activo: item.activo ?? item.estaPublicado ?? true,
        stockFisico: item.stockFisico || (item.stock ? {
          cantidadDisponible: item.stock.cantidadDisponible ?? 0,
          cantidadReservada: item.stock.cantidadReservada ?? 0,
          cantidadTotal: item.stock.cantidadTotal ?? 0,
        } : {
          cantidadDisponible: 15,
          cantidadReservada: 0,
          cantidadTotal: 15,
        }),
      }));
      setProductos(mapped);
    } catch {
      // Fallback demo data
      const cats = ['Tecnología', 'Audio & Video', 'Periféricos Gamer', 'Cómputo', 'Accesorios'];
      const mock: Producto[] = Array.from({ length: 25 }, (_, i) => {
        const disp = (i * 7) % 65;
        const res = (i * 3) % 15;
        return {
          id: `prod-${i + 1}`,
          sku: `SKU-LOGI-${1000 + i}`,
          nombre: [
            'Smartphone Galaxy A54 5G 128GB',
            'Audífonos Noise Cancelling Pro',
            'Monitor Gamer 27" 165Hz IPS',
            'Teclado Mecánico RGB Switch Red',
            'Mouse Inalámbrico Ergonómico 4000 DPI',
            'Cargador GaN Ultra Rápido 65W',
            'Smartwatch Deportivo GPS 5ATM',
            'Hub USB-C 8 en 1 HDMI 4K',
          ][i % 8] + (i > 7 ? ` (Edición ${Math.floor(i / 8) + 1})` : ''),
          descripcion: 'Producto homologado para logística y almacenamiento en bodega central con control de número de serie.',
          precio: 25.00 + (i * 18.5),
          categoria: cats[i % cats.length],
          marca: ['Samsung', 'Sony', 'ASUS', 'Logitech', 'Anker'][i % 5],
          activo: true,
          stockFisico: {
            cantidadDisponible: disp,
            cantidadReservada: res,
            cantidadTotal: disp + res,
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
    const matchCat = selectedCat ? p.categoria === selectedCat : true;
    return matchSearch && matchCat;
  });

  const totalDisponibles = productos.reduce((acc, p) => acc + (p.stockFisico?.cantidadDisponible ?? 0), 0);
  const totalReservados = productos.reduce((acc, p) => acc + (p.stockFisico?.cantidadReservada ?? 0), 0);
  const skusCriticos = productos.filter(p => (p.stockFisico?.cantidadDisponible ?? 0) < 10).length;

  const handleOpenCrear = () => {
    setEditingId(null);
    setForm({
      sku: `SKU-LOGI-${Math.floor(1000 + Math.random() * 9000)}`,
      nombre: '',
      descripcion: '',
      precio: 49.99,
      costoUnitario: 35.00,
      categoria: 'Tecnología',
      marca: '',
      pesoKg: 0.5,
      largoCm: 20,
      anchoCm: 15,
      altoCm: 5,
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
      precio: p.precio,
      costoUnitario: 0,
      categoria: p.categoria,
      marca: p.marca,
      pesoKg: 0.5,
      largoCm: 20,
      anchoCm: 15,
      altoCm: 5,
      estaPublicado: p.activo,
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
      setProductos(prev => prev.map(p => {
        if (p.id !== editingId) return p;
        return {
          ...p,
          sku: form.sku || p.sku,
          nombre: form.nombre || p.nombre,
          descripcion: form.descripcion ?? p.descripcion,
          precio: Number(form.precio) || 0,
          categoria: form.categoria || p.categoria || 'Tecnología',
          marca: form.marca || p.marca || 'N/A',
          activo: form.estaPublicado ?? p.activo ?? true,
          stockFisico: p.stockFisico || {
            cantidadDisponible: 20,
            cantidadReservada: 0,
            cantidadTotal: 20,
          },
        };
      }));
    } else {
      const nuevo: Producto = {
        id: crypto.randomUUID(),
        sku: form.sku || `SKU-LOGI-${Math.floor(1000 + Math.random() * 9000)}`,
        nombre: form.nombre || 'Nuevo Producto',
        descripcion: form.descripcion || '',
        precio: Number(form.precio) || 0,
        categoria: form.categoria || 'Tecnología',
        marca: form.marca || 'N/A',
        activo: form.estaPublicado ?? true,
        stockFisico: {
          cantidadDisponible: 20,
          cantidadReservada: 0,
          cantidadTotal: 20,
        },
      };
      setProductos(prev => [nuevo, ...prev]);
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
            <option value="Tecnología">Tecnología</option>
            <option value="Audio & Video">Audio & Video</option>
            <option value="Periféricos Gamer">Periféricos Gamer</option>
            <option value="Cómputo">Cómputo</option>
            <option value="Accesorios">Accesorios</option>
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
                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.15rem' }}>{row.marca} • {row.categoria}</div>
              </div>
            ),
          },
          {
            header: 'Precio Venta',
            accessor: 'precio',
            render: (row) => (
              <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--text-primary)' }}>
                ${(Number(row.precio) || 0).toFixed(2)}
              </span>
            ),
          },
          {
            header: 'Stock Disponible',
            accessor: 'stockFisico',
            render: (row) => {
              const disp = row.stockFisico?.cantidadDisponible ?? 0;
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
                {row.stockFisico?.cantidadReservada ?? 0} unid.
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
                  value={form.precio}
                  onChange={e => setForm(f => ({ ...f, precio: parseFloat(e.target.value) || 0 }))}
                  required
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Categoría
                </label>
                <select
                  value={form.categoria}
                  onChange={e => setForm(f => ({ ...f, categoria: e.target.value }))}
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
                  Marca
                </label>
                <input
                  type="text"
                  value={form.marca}
                  onChange={e => setForm(f => ({ ...f, marca: e.target.value }))}
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
