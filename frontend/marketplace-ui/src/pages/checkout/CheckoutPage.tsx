/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState } from 'react';
import { ShoppingCart, CheckCircle2, Package, CreditCard, AlertCircle } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import { productosApi } from '../../api/productos';
import { apiClient } from '../../api/client';
import type { Producto } from '../../types';

export default function CheckoutPage() {
  const { usuario } = useAuth();
  const toast = useToast();
  const [productos, setProductos] = useState<Producto[]>([]);
  const [loading, setLoading] = useState(true);
  const [carrito, setCarrito] = useState<{ producto: Producto; cantidad: number }[]>([]);
  const [procesando, setProcesando] = useState(false);
  const [ordenCompletada, setOrdenCompletada] = useState<string | null>(null);

  useEffect(() => {
    let cancel = false;
    productosApi.listar(1, 20)
      .then(res => {
        if (cancel) return;
        const items = res.items || (Array.isArray(res) ? res : []);
        setProductos(items);
      })
      .catch(() => {
        if (cancel) return;
        // Demo products fallback
        setProductos([
          {
            id: 'demo-prod-1',
            sku: 'SKU-LOGI-8801',
            nombre: 'Kit Logístico Starter (Seed SKU)',
            descripcion: 'Equipamiento básico para e-commerce',
            precio: 45.00,
            categoria: 'Tecnología',
            marca: 'LOGIMKT',
            activo: true,
          },
          {
            id: 'demo-prod-2',
            sku: 'SKU-LOGI-8802',
            nombre: 'Escáner Código de Barras Láser 1D/2D',
            descripcion: 'Escáner industrial con soporte USB',
            precio: 85.50,
            categoria: 'Equipamiento',
            marca: 'LOGIMKT Pro',
            activo: true,
          },
        ]);
      })
      .finally(() => {
        if (!cancel) setLoading(false);
      });
    return () => { cancel = true; };
  }, []);

  const agregarAlCarrito = (prod: Producto) => {
    setCarrito(prev => {
      const idx = prev.findIndex(item => item.producto.id === prod.id);
      if (idx >= 0) {
        const clon = [...prev];
        clon[idx].cantidad += 1;
        return clon;
      }
      return [...prev, { producto: prod, cantidad: 1 }];
    });
    toast.success('Producto agregado', `${prod.nombre} fue añadido al carrito.`);
  };

  const quitarDelCarrito = (prodId: string) => {
    setCarrito(prev => prev.filter(item => item.producto.id !== prodId));
  };

  const total = carrito.reduce((acc, item) => acc + (item.producto.precio * item.cantidad), 0);

  const handleCheckout = async () => {
    if (carrito.length === 0) return;
    setProcesando(true);
    setOrdenCompletada(null);

    const payload = {
      direccionEnvio: 'Calle Principal #123, San Salvador, El Salvador',
      zonaEnvio: 'San Salvador',
      metodoPago: 'TarjetaCredito',
      items: carrito.map(c => ({
        productoId: c.producto.id,
        sku: c.producto.sku,
        cantidad: c.cantidad,
        precioUnitario: c.producto.precio,
      })),
    };

    try {
      const res = await apiClient.post<{ numeroOrden?: string; id?: string }>('/api/checkout', payload);
      const num = res.numeroOrden || res.id || `ORD-${Date.now()}`;
      setOrdenCompletada(num);
      setCarrito([]);
      toast.success('¡Compra confirmada!', `Tu orden ${num} ha sido generada exitosamente.`);
    } catch (err: any) {
      toast.error('Error al procesar compra', err?.message || 'No se pudo completar el checkout.');
    } finally {
      setProcesando(false);
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', maxWidth: 1200, margin: '0 auto' }}>
      <div className="page-header">
        <div>
          <h1 className="page-title">Catálogo de Compra & Checkout</h1>
          <p className="page-subtitle">Portal exclusivo para compradores — Selección y compra directa de productos</p>
        </div>
      </div>

      {ordenCompletada && (
        <div style={{
          background: 'rgba(34, 197, 94, 0.1)',
          border: '1px solid var(--green)',
          borderRadius: 'var(--radius-md)',
          padding: '1rem 1.25rem',
          display: 'flex',
          alignItems: 'center',
          gap: '0.75rem',
          color: 'var(--green)',
        }}>
          <CheckCircle2 size={24} />
          <div>
            <div style={{ fontWeight: 700, fontSize: '1rem' }}>Orden generada correctamente</div>
            <div style={{ fontSize: '0.85rem' }}>Identificador de pedido: <strong>{ordenCompletada}</strong></div>
          </div>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '1.5rem' }}>
        {/* Catálogo */}
        <div>
          <h2 style={{ fontSize: '1.1rem', fontWeight: 700, marginBottom: '1rem', color: 'var(--text-primary)' }}>
            Productos Disponibles
          </h2>
          {loading ? (
            <div style={{ color: 'var(--text-muted)', padding: '2rem' }}>Cargando catálogo...</div>
          ) : productos.length === 0 ? (
            <div style={{ color: 'var(--text-muted)', padding: '2rem' }}>No hay productos en el catálogo.</div>
          ) : (
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: '1rem' }}>
              {productos.map(p => (
                <div
                  key={p.id}
                  style={{
                    background: 'var(--bg-card)',
                    border: '1px solid var(--border)',
                    borderRadius: 'var(--radius-md)',
                    padding: '1rem',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                  }}
                >
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                      <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>{p.sku}</span>
                      <span style={{ fontSize: '0.75rem', color: 'var(--accent)', fontWeight: 600 }}>{p.categoria}</span>
                    </div>
                    <div style={{ fontWeight: 700, color: 'var(--text-primary)', marginBottom: '0.35rem' }}>{p.nombre}</div>
                    <div style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', marginBottom: '0.75rem', minHeight: 36 }}>
                      {p.descripcion || 'Sin descripción detallada.'}
                    </div>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '0.5rem', paddingTop: '0.5rem', borderTop: '1px solid var(--border)' }}>
                    <div style={{ fontSize: '1.15rem', fontWeight: 800, color: 'var(--green)' }}>
                      ${p.precio.toFixed(2)}
                    </div>
                    <button
                      className="btn btn-secondary btn-sm"
                      onClick={() => agregarAlCarrito(p)}
                    >
                      <ShoppingCart size={13} />
                      Comprar
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Resumen del Carrito */}
        <div>
          <div style={{
            background: 'var(--bg-card)',
            border: '1px solid var(--border)',
            borderRadius: 'var(--radius-lg)',
            padding: '1.25rem',
            position: 'sticky',
            top: '1rem',
          }}>
            <h2 style={{ fontSize: '1.1rem', fontWeight: 700, marginBottom: '1rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <ShoppingCart size={18} />
              Carrito de Compra ({carrito.reduce((a, b) => a + b.cantidad, 0)})
            </h2>

            {carrito.length === 0 ? (
              <div style={{ color: 'var(--text-muted)', fontSize: '0.9rem', padding: '1.5rem 0', textAlign: 'center' }}>
                Tu carrito está vacío. Agrega productos desde el catálogo para iniciar el checkout.
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                {carrito.map(item => (
                  <div
                    key={item.producto.id}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      fontSize: '0.85rem',
                      paddingBottom: '0.5rem',
                      borderBottom: '1px solid var(--border)',
                    }}
                  >
                    <div style={{ flex: 1, minWidth: 0, paddingRight: '0.5rem' }}>
                      <div style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {item.producto.nombre}
                      </div>
                      <div style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>
                        {item.cantidad} x ${item.producto.precio.toFixed(2)}
                      </div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <span style={{ fontWeight: 700, fontFamily: 'var(--font-mono)' }}>
                        ${(item.producto.precio * item.cantidad).toFixed(2)}
                      </span>
                      <button
                        onClick={() => quitarDelCarrito(item.producto.id)}
                        style={{
                          background: 'none',
                          border: 'none',
                          color: 'var(--red)',
                          cursor: 'pointer',
                          fontSize: '0.8rem',
                        }}
                      >
                        ✕
                      </button>
                    </div>
                  </div>
                ))}

                <div style={{ marginTop: '0.5rem', paddingTop: '0.5rem', display: 'flex', justifyContent: 'space-between', fontSize: '1.1rem', fontWeight: 800 }}>
                  <span>Total</span>
                  <span style={{ color: 'var(--green)' }}>${total.toFixed(2)}</span>
                </div>

                <div style={{ fontSize: '0.75rem', color: 'var(--text-secondary)', marginTop: '0.5rem' }}>
                  Comprador: <strong>{usuario?.nombre || usuario?.email}</strong>
                </div>

                <button
                  className="btn btn-primary"
                  onClick={handleCheckout}
                  disabled={procesando}
                  style={{ width: '100%', justifyContent: 'center', marginTop: '1rem' }}
                >
                  <CreditCard size={15} />
                  {procesando ? 'Procesando Pago...' : 'Confirmar Orden de Compra'}
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
