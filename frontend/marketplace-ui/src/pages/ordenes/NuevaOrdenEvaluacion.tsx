import React, { useEffect, useRef, useState } from 'react';
import { apiClient, ApiError } from '../../api/client';
import Modal from '../../components/ui/Modal';
import type { CrearOrdenRequest } from '../../types';

type Opcion = { id: string; nombre: string };
type Producto = Opcion & { vendedorId: string; sku: string; precioB2C: number; precioB2B: number };
type Opciones = { compradores: Opcion[]; vendedores: Opcion[]; productos: Producto[] };
type Solicitud = { key: string; payload: Omit<CrearOrdenRequest, 'tipoCliente'> & { tipoCliente: 0 | 1 } };

export default function NuevaOrdenEvaluacion({ actorId, onCreated }: { actorId: string; onCreated: () => void }) {
  const storageKey = `erp_oms_evaluacion_v1:${actorId}`;
  const [options, setOptions] = useState<Opciones | null>(null);
  const [open, setOpen] = useState(false);
  const [buyer, setBuyer] = useState('');
  const [seller, setSeller] = useState('');
  const [channel, setChannel] = useState<'B2B' | 'B2C'>('B2C');
  const [address, setAddress] = useState('Entrega sintética de evaluación STAGING');
  const [lines, setLines] = useState([{ productoId: '', cantidad: 1 }]);
  const [pending, setPending] = useState<Solicitud | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [error, setError] = useState('');
  const [canCorrect, setCanCorrect] = useState(false);
  const [availability, setAvailability] = useState('Comprobando disponibilidad…');

  useEffect(() => {
    let active = true;
    try {
      const saved = localStorage.getItem(storageKey);
      if (saved) {
        const parsed: Solicitud = JSON.parse(saved);
        if (!parsed.key || !parsed.payload?.items?.length) throw new Error();
        setPending(parsed);
      }
    } catch { setAvailability('No se pudo recuperar la solicitud pendiente. Requiere revisión.'); return; }
    apiClient.get<Opciones>('/api/ordenes/evaluacion/opciones').then(value => {
      if (!active) return;
      if (!Array.isArray(value.compradores) || !Array.isArray(value.vendedores) || !Array.isArray(value.productos)) throw new Error();
      setOptions(value);
      setAvailability(value.compradores.length && value.vendedores.length && value.productos.length
        ? '' : 'Faltan perfiles o productos habilitados para la evaluación.');
    }).catch(() => { if (active) setAvailability('Creación de evaluación no disponible en este entorno.'); });
    return () => { active = false; };
  }, [storageKey]);

  const send = async () => {
    if (busyRef.current || !options) return;
    busyRef.current = true;
    setBusy(true); setError(''); setCanCorrect(false);
    try {
      const operation: Solicitud = pending ?? {
        key: crypto.randomUUID(),
        payload: {
          compradorId: buyer, vendedorId: seller, tipoCliente: channel === 'B2B' ? 1 : 0,
          direccionEntrega: address.trim(), costoEnvio: 5,
          items: lines.map(line => ({ ...line, precioUnitario: 0 })),
        },
      };
      // Persistir ANTES de enviar: un cierre/timeout reutiliza exactamente la misma clave y cuerpo.
      localStorage.setItem(storageKey, JSON.stringify(operation));
      setPending(operation);
      const result = await apiClient.post<{ id: string }>('/api/ordenes', operation.payload,
        { 'Idempotency-Key': operation.key });
      if (typeof result.id !== 'string') throw new Error('Respuesta sin identificador; reintenta la misma solicitud.');
      localStorage.removeItem(storageKey);
      setPending(null); setOpen(false); onCreated();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Solicitud pendiente de verificación.');
      // Solo los rechazos inequívocos anteriores al commit permiten cambiar el cuerpo.
      setCanCorrect(failure instanceof ApiError && (failure.status === 400 ||
        (failure.status === 409 && failure.message.startsWith('Stock insuficiente;'))));
    } finally { busyRef.current = false; setBusy(false); }
  };

  const valid = buyer && seller && address.trim() && lines.length > 0 && lines.every(line =>
    Number.isInteger(line.cantidad) && line.cantidad > 0 && line.cantidad <= 10000 &&
    options?.productos.some(product => product.id === line.productoId && product.vendedorId === seller)) &&
    new Set(lines.map(line => line.productoId)).size === lines.length;

  return <>
    <button className="btn btn-primary" disabled={!!availability || !options} title={availability}
      onClick={() => setOpen(true)}>{pending ? 'Revisar orden pendiente' : '+ Nueva Orden'}</button>
    {availability && <span role="status" style={{ maxWidth: 220, fontSize: 12 }}>{availability}</span>}
    <Modal title="Nueva orden de evaluación" open={open} onClose={() => { if (!busy) setOpen(false); }} size="lg">
      <p>Datos sintéticos de STAGING. Se reservará inventario sin realizar pagos.</p>
      {pending ? <p role="status">Existe una solicitud enviada. Reintentar consulta o completa esa misma orden sin duplicarla. No cambies sus datos hasta verificar el resultado.</p> : <>
        <label>Comprador<select aria-label="Comprador" className="form-input" value={buyer} onChange={e => setBuyer(e.target.value)}>
          <option value="">Seleccionar comprador</option>{options?.compradores.map(o => <option key={o.id} value={o.id}>{o.nombre}</option>)}</select></label>
        <label>Vendedor<select aria-label="Vendedor" className="form-input" value={seller} onChange={e => { setSeller(e.target.value); setLines([{ productoId: '', cantidad: 1 }]); }}>
          <option value="">Seleccionar vendedor</option>{options?.vendedores.map(o => <option key={o.id} value={o.id}>{o.nombre}</option>)}</select></label>
        <label>Canal<select aria-label="Canal" className="form-input" value={channel} onChange={e => setChannel(e.target.value as 'B2B' | 'B2C')}>
          <option value="B2C">B2C (mínimo $5)</option><option value="B2B">B2B (mínimo $500)</option></select></label>
        <label>Dirección de prueba<input className="form-input" maxLength={500} value={address} onChange={e => setAddress(e.target.value)} /></label>
        {lines.map((line, index) => <div key={index} style={{ display: 'flex', gap: 8, marginTop: 8 }}>
          <select aria-label={`Producto ${index + 1}`} className="form-input" value={line.productoId}
            onChange={e => setLines(lines.map((x, i) => i === index ? { ...x, productoId: e.target.value } : x))}>
            <option value="">Seleccionar producto</option>{options?.productos.filter(p => p.vendedorId === seller).map(p =>
              <option key={p.id} value={p.id}>{p.sku} — {p.nombre} (${channel === 'B2B' ? p.precioB2B : p.precioB2C})</option>)}</select>
          <input aria-label={`Cantidad ${index + 1}`} className="form-input" type="number" min={1} max={10000} value={line.cantidad}
            onChange={e => setLines(lines.map((x, i) => i === index ? { ...x, cantidad: Number(e.target.value) } : x))} />
          <button className="btn btn-secondary" onClick={() => setLines(lines.filter((_, i) => i !== index))}>Quitar</button>
        </div>)}
        <button className="btn btn-secondary" disabled={lines.length >= 20} onClick={() => setLines([...lines, { productoId: '', cantidad: 1 }])}>Añadir producto</button>
      </>}
      {error && <p role="alert">{error}</p>}
      {canCorrect && !busy && <button className="btn btn-secondary" onClick={() => {
        try { localStorage.removeItem(storageKey); setPending(null); setCanCorrect(false); setError(''); }
        catch { setError('No se pudo actualizar el borrador.'); }
      }}>Corregir solicitud rechazada</button>}
      <p>El servidor calcula los precios vigentes, IVA del 13 % y envío de $5.</p>
      <button className="btn btn-primary" disabled={busy || (!pending && !valid)} onClick={() => void send()}>
        {busy ? 'Verificando…' : pending ? 'Reintentar la misma solicitud' : 'Crear y reservar'}</button>
    </Modal>
  </>;
}
