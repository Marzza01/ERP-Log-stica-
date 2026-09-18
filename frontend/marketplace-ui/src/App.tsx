import { useState, useEffect } from 'react';
import { 
  BarChart3, 
  Warehouse, 
  RotateCcw, 
  FileCheck2, 
  TrendingUp, 
  Scale, 
  Boxes, 
  ArrowUpRight, 
  CheckCircle2, 
  AlertCircle, 
  Clock, 
  Search, 
  Sparkles,
  Layers,
  ShieldCheck,
  Building2,
  DollarSign
} from 'lucide-react';

interface KpiItem {
  nombre: string;
  valor: number;
  unidad: string;
  meta: string;
  semaforo: 'Verde' | 'Amarillo' | 'Rojo';
  descripcion: string;
}

interface KpisResponse {
  periodoDias: number;
  fechaCálculo: string;
  kpis: KpiItem[];
}

interface ProyeccionItem {
  mes: number;
  nombreMes: string;
  ordenesProyectadas: number;
  gmvProyectado: number;
  ingresosComisionPlataforma: number;
  costoOperativoFulfillment: number;
  margenOperativoNeto: number;
}

interface ProductoItem {
  sku: string;
  nombre: string;
  categoria: string;
  precioB2C: number;
  precioB2B: number;
  stock: number;
  stockMinimo: number;
  pasillo: string;
  rack: string;
  nivel: number;
  posicion: string;
}

interface PickListItem {
  id: string;
  numeroPickList: string;
  numeroOrden: string;
  operador: string;
  estado: 'Pendiente' | 'EnProceso' | 'Completado';
  totalItems: number;
  itemsCompletados: number;
  ruta: string;
}

interface RmaItem {
  id: string;
  numeroRma: string;
  numeroOrden: string;
  sku: string;
  producto: string;
  cantidad: number;
  motivoCliente: string;
  estado: 'Solicitado' | 'EnInspeccion' | 'Dictaminado' | 'Completado';
  clasificacion: 'Pendiente' | 'ReStock' | 'MermaScrap' | 'Remate';
  inspector: string;
  impactoContable: string;
}

interface MatchAuditItem {
  id: string;
  numeroPo: string;
  proveedor: string;
  sku: string;
  poCantidad: number;
  poPrecio: number;
  wmsCantidad: number;
  facturaCantidad: number;
  facturaPrecio: number;
  discrepancia: 'Ninguna' | 'Cantidad' | 'Precio' | 'Ambas';
  estado: 'Conforme' | 'Discrepancia' | 'AprobadoManual';
  autorizadoPago: boolean;
}

const mockKpis: KpiItem[] = [
  { nombre: "Order Fill Rate", valor: 97.4, unidad: "%", meta: "≥ 95%", semaforo: "Verde", descripcion: "Cumplimiento total de órdenes desde almacén central." },
  { nombre: "OTIF Rate (On-Time In-Full)", valor: 92.8, unidad: "%", meta: "≥ 90%", semaforo: "Verde", descripcion: "Entregas a tiempo y completas según SLA." },
  { nombre: "Order Lead Time (Ciclo)", valor: 18.2, unidad: "Horas", meta: "≤ 24h", semaforo: "Verde", descripcion: "Tiempo transcurrido entre aprobación y despacho listo." },
  { nombre: "Tasa de RMA (Logística Inversa)", valor: 1.6, unidad: "%", meta: "≤ 2.0%", semaforo: "Verde", descripcion: "Retornos sobre el volumen total de órdenes." },
  { nombre: "Rotación de Inventario WMS", valor: 8.4, unidad: "x/año", meta: "≥ 6.0", semaforo: "Verde", descripcion: "Rotación de existencias en andenes y racks." },
  { nombre: "Conformidad 3-Way Match", valor: 98.6, unidad: "%", meta: "≥ 98%", semaforo: "Verde", descripcion: "Auditorías de compra sin discrepancias." },
  { nombre: "Margen Comisión Split 90/10", valor: 10.0, unidad: "%", meta: "10.0%", semaforo: "Verde", descripcion: "Retención neta sobre ventas marketplace." },
  { nombre: "DPO (Plazo Crédito Proveedor)", valor: 42.0, unidad: "Días", meta: "30 - 60", semaforo: "Verde", descripcion: "Días promedio de pago a proveedores salvadoreños." }
];

const mockProyeccion: ProyeccionItem[] = [
  { mes: 1, nombreMes: "Mes 1", ordenesProyectadas: 1200, gmvProyectado: 48000, ingresosComisionPlataforma: 4800, costoOperativoFulfillment: 1800, margenOperativoNeto: 3000 },
  { mes: 2, nombreMes: "Mes 2", ordenesProyectadas: 1450, gmvProyectado: 58000, ingresosComisionPlataforma: 5800, costoOperativoFulfillment: 2100, margenOperativoNeto: 3700 },
  { mes: 3, nombreMes: "Mes 3", ordenesProyectadas: 1750, gmvProyectado: 70000, ingresosComisionPlataforma: 7000, costoOperativoFulfillment: 2400, margenOperativoNeto: 4600 },
  { mes: 4, nombreMes: "Mes 4", ordenesProyectadas: 2100, gmvProyectado: 84000, ingresosComisionPlataforma: 8400, costoOperativoFulfillment: 2800, margenOperativoNeto: 5600 },
  { mes: 5, nombreMes: "Mes 5", ordenesProyectadas: 2500, gmvProyectado: 100000, ingresosComisionPlataforma: 10000, costoOperativoFulfillment: 3200, margenOperativoNeto: 6800 },
  { mes: 6, nombreMes: "Mes 6", ordenesProyectadas: 3000, gmvProyectado: 120000, ingresosComisionPlataforma: 12000, costoOperativoFulfillment: 3700, margenOperativoNeto: 8300 },
  { mes: 7, nombreMes: "Mes 7", ordenesProyectadas: 3600, gmvProyectado: 144000, ingresosComisionPlataforma: 14400, costoOperativoFulfillment: 4300, margenOperativoNeto: 10100 },
  { mes: 8, nombreMes: "Mes 8", ordenesProyectadas: 4200, gmvProyectado: 168000, ingresosComisionPlataforma: 16800, costoOperativoFulfillment: 4900, margenOperativoNeto: 11900 },
  { mes: 9, nombreMes: "Mes 9", ordenesProyectadas: 5000, gmvProyectado: 200000, ingresosComisionPlataforma: 20000, costoOperativoFulfillment: 5600, margenOperativoNeto: 14400 },
  { mes: 10, nombreMes: "Mes 10", ordenesProyectadas: 6000, gmvProyectado: 240000, ingresosComisionPlataforma: 24000, costoOperativoFulfillment: 6400, margenOperativoNeto: 17600 },
  { mes: 11, nombreMes: "Mes 11", ordenesProyectadas: 7200, gmvProyectado: 288000, ingresosComisionPlataforma: 28800, costoOperativoFulfillment: 7400, margenOperativoNeto: 21400 },
  { mes: 12, nombreMes: "Mes 12", ordenesProyectadas: 8800, gmvProyectado: 352000, ingresosComisionPlataforma: 35200, costoOperativoFulfillment: 8700, margenOperativoNeto: 26500 }
];

const mockProductos: ProductoItem[] = [
  { sku: "ELC-0001", nombre: "Smartphone Pro 128GB Standard", categoria: "Electrónica y Tecnología", precioB2C: 282.24, precioB2B: 225.79, stock: 29, stockMinimo: 14, pasillo: "A", rack: "01", nivel: 1, posicion: "01" },
  { sku: "ELC-0002", nombre: "Cable USB-C Carga Rápida 2m", categoria: "Electrónica y Tecnología", precioB2C: 24.69, precioB2B: 19.75, stock: 63, stockMinimo: 15, pasillo: "A", rack: "02", nivel: 2, posicion: "04" },
  { sku: "FER-0050", nombre: "Taladro Percutor Industrial 800W", categoria: "Ferretería e Industria", precioB2C: 115.00, precioB2B: 92.00, stock: 45, stockMinimo: 10, pasillo: "B", rack: "01", nivel: 1, posicion: "02" },
  { sku: "HOG-0120", nombre: "Juego de Sábanas 100% Algodón King", categoria: "Hogar y Decoración", precioB2C: 48.50, precioB2B: 38.80, stock: 80, stockMinimo: 20, pasillo: "C", rack: "03", nivel: 3, posicion: "01" },
  { sku: "ALI-0210", nombre: "Café Gourmet de Altura 1kg", categoria: "Alimentos y Bebidas", precioB2C: 14.50, precioB2B: 11.60, stock: 120, stockMinimo: 30, pasillo: "D", rack: "01", nivel: 1, posicion: "05" }
];

const initialPickLists: PickListItem[] = [
  { id: "1", numeroPickList: "PKL-20260918-1001", numeroOrden: "ORD-2026-901", operador: "Carlos Gómez (Bodega A)", estado: "EnProceso", totalItems: 4, itemsCompletados: 3, ruta: "Pasillo A ➔ Pasillo B" },
  { id: "2", numeroPickList: "PKL-20260918-1002", numeroOrden: "ORD-2026-902", operador: "Ana Martínez (Bodega B)", estado: "Pendiente", totalItems: 2, itemsCompletados: 0, ruta: "Pasillo C ➔ Pasillo D" },
  { id: "3", numeroPickList: "PKL-20260918-1003", numeroOrden: "ORD-2026-903", operador: "Roberto Flores (Bodega Central)", estado: "Completado", totalItems: 5, itemsCompletados: 5, ruta: "Pasillo A ➔ Pasillo C" }
];

const initialRmas: RmaItem[] = [
  { id: "1", numeroRma: "RMA-2026-0081", numeroOrden: "ORD-2026-880", sku: "FER-0050", producto: "Taladro Percutor Industrial 800W", cantidad: 1, motivoCliente: "Empaque exterior golpeado en transporte", estado: "EnInspeccion", clasificacion: "Pendiente", inspector: "Ing. Jorge Rivas", impactoContable: "En custodia técnica" },
  { id: "2", numeroRma: "RMA-2026-0082", numeroOrden: "ORD-2026-884", sku: "ELC-0002", producto: "Cable USB-C Carga Rápida 2m", cantidad: 2, motivoCliente: "Cliente ordenó modelo equivocado", estado: "Dictaminado", clasificacion: "ReStock", inspector: "Ing. Jorge Rivas", impactoContable: "Reingreso a Stock 1108 / Nota de Crédito" },
  { id: "3", numeroRma: "RMA-2026-0083", numeroOrden: "ORD-2026-890", sku: "ELC-0001", producto: "Smartphone Pro 128GB Standard", cantidad: 1, motivoCliente: "Falla de encendido interno de fábrica", estado: "Dictaminado", clasificacion: "MermaScrap", inspector: "Ing. Jorge Rivas", impactoContable: "Imputación a 5101 Pérdida por Merma" }
];

const initialMatches: MatchAuditItem[] = [
  { id: "1", numeroPo: "PO-2026-0101", proveedor: "Distribuidora Morazán S.A.", sku: "FER-0050", poCantidad: 50, poPrecio: 92.00, wmsCantidad: 50, facturaCantidad: 50, facturaPrecio: 92.00, discrepancia: "Ninguna", estado: "Conforme", autorizadoPago: true },
  { id: "2", numeroPo: "PO-2026-0102", proveedor: "Telecomunicaciones de C.A.", sku: "ELC-0001", poCantidad: 20, poPrecio: 225.79, wmsCantidad: 18, facturaCantidad: 20, facturaPrecio: 225.79, discrepancia: "Cantidad", estado: "Discrepancia", autorizadoPago: false },
  { id: "3", numeroPo: "PO-2026-0103", proveedor: "Importadora San Salvador", sku: "HOG-0120", poCantidad: 40, poPrecio: 38.80, wmsCantidad: 40, facturaCantidad: 40, facturaPrecio: 42.00, discrepancia: "Precio", estado: "Discrepancia", autorizadoPago: false }
];

export function App() {
  const [activeTab, setActiveTab] = useState<'kpis' | 'wms' | 'rma' | 'match' | 'proyeccion' | 'contabilidad'>('kpis');
  const [kpis, setKpis] = useState<KpiItem[]>(mockKpis);
  const [pickLists, setPickLists] = useState<PickListItem[]>(initialPickLists);
  const [rmas, setRmas] = useState<RmaItem[]>(initialRmas);
  const [matches, setMatches] = useState<MatchAuditItem[]>(initialMatches);
  const [simulacionActiva, setSimulacionActiva] = useState<boolean>(false);
  const [filtroSku, setFiltroSku] = useState<string>('');

  useEffect(() => {
    fetch('/api/kpis/dashboard?dias=30')
      .then(res => {
        if (!res.ok) throw new Error('API no conectada');
        return res.json();
      })
      .then((data: KpisResponse) => {
        if (data && data.kpis && data.kpis.length > 0) {
          setKpis(data.kpis);
        }
      })
      .catch(() => {
        console.info('Modo operativo: visualizando registros en memoria.');
      });
  }, []);

  const handleSimularOrden = () => {
    setSimulacionActiva(true);
    const nuevoId = (pickLists.length + 1).toString();
    const nuevaPickList: PickListItem = {
      id: nuevoId,
      numeroPickList: `PKL-${new Date().toISOString().slice(0,10).replace(/-/g,'')}-${Math.floor(1000 + Math.random() * 9000)}`,
      numeroOrden: `ORD-SV-${Math.floor(1000 + Math.random() * 9000)}`,
      operador: "Operador WMS Automático",
      estado: "Pendiente",
      totalItems: 3,
      itemsCompletados: 0,
      ruta: "Pasillo A ➔ Pasillo B ➔ Pasillo C"
    };

    setPickLists([nuevaPickList, ...pickLists]);
    setTimeout(() => {
      setSimulacionActiva(false);
    }, 400);
  };

  const handleCompletarPickList = (id: string) => {
    setPickLists(pickLists.map(p => 
      p.id === id ? { ...p, estado: 'Completado', itemsCompletados: p.totalItems } : p
    ));
  };

  const handleDictaminarRma = (id: string, clasificacion: 'ReStock' | 'MermaScrap' | 'Remate') => {
    setRmas(rmas.map(r => {
      if (r.id !== id) return r;
      let impacto = '';
      if (clasificacion === 'ReStock') impacto = 'Reingreso automático a Stock 1108 / Nota de Crédito emitida';
      else if (clasificacion === 'MermaScrap') impacto = 'Baja de inventario / Imputación a 5101 Pérdida por Merma';
      else impacto = 'Reubicación en Zona Remates con 30% descuento';
      return {
        ...r,
        estado: 'Dictaminado',
        clasificacion,
        impactoContable: impacto
      };
    }));
  };

  const handleAprobarMatchManual = (id: string) => {
    setMatches(matches.map(m => 
      m.id === id ? { ...m, estado: 'AprobadoManual', autorizadoPago: true } : m
    ));
  };

  const productosFiltrados = mockProductos.filter(p => 
    p.nombre.toLowerCase().includes(filtroSku.toLowerCase()) || 
    p.sku.toLowerCase().includes(filtroSku.toLowerCase()) ||
    p.categoria.toLowerCase().includes(filtroSku.toLowerCase())
  );

  return (
    <div className="erp-app-shell">
      {/* ── BARRA LATERAL (SIDEBAR CORPORATIVA - EFFORD/SHADCN SHELL) ────────────── */}
      <aside className="erp-sidebar">
        <div className="erp-brand-header">
          <div className="erp-brand-logo">
            <Building2 size={20} color="#fff" />
          </div>
          <div className="erp-brand-info">
            <h2>LOGIMKT S.A.</h2>
            <p>Marketplace & ERP Core</p>
          </div>
        </div>

        <div className="erp-nav-section-title">Operaciones & Almacén</div>
        <ul className="erp-nav-list">
          <li>
            <button 
              className={`erp-nav-item ${activeTab === 'kpis' ? 'active' : ''}`}
              onClick={() => setActiveTab('kpis')}
            >
              <BarChart3 size={18} />
              <span>Panel Ejecutivo KPIs</span>
            </button>
          </li>
          <li>
            <button 
              className={`erp-nav-item ${activeTab === 'wms' ? 'active' : ''}`}
              onClick={() => setActiveTab('wms')}
            >
              <Warehouse size={18} />
              <span>Fulfillment & WMS</span>
            </button>
          </li>
          <li>
            <button 
              className={`erp-nav-item ${activeTab === 'rma' ? 'active' : ''}`}
              onClick={() => setActiveTab('rma')}
            >
              <RotateCcw size={18} />
              <span>Logística Inversa (RMA)</span>
            </button>
          </li>
        </ul>

        <div className="erp-nav-section-title">Finanzas & Control</div>
        <ul className="erp-nav-list">
          <li>
            <button 
              className={`erp-nav-item ${activeTab === 'match' ? 'active' : ''}`}
              onClick={() => setActiveTab('match')}
            >
              <FileCheck2 size={18} />
              <span>Auditoría 3-Way Match</span>
            </button>
          </li>
          <li>
            <button 
              className={`erp-nav-item ${activeTab === 'proyeccion' ? 'active' : ''}`}
              onClick={() => setActiveTab('proyeccion')}
            >
              <TrendingUp size={18} />
              <span>Modelo Financiero 12M</span>
            </button>
          </li>
          <li>
            <button 
              className={`erp-nav-item ${activeTab === 'contabilidad' ? 'active' : ''}`}
              onClick={() => setActiveTab('contabilidad')}
            >
              <Scale size={18} />
              <span>Contabilidad (Partida Doble)</span>
            </button>
          </li>
        </ul>

        <div className="erp-sidebar-footer">
          <div className="user-avatar">
            <ShieldCheck size={18} color="#94A3B8" />
          </div>
          <div className="user-meta">
            <h4>Administrador Logístico</h4>
            <span>Nodo Central Operativo</span>
          </div>
        </div>
      </aside>

      {/* ── ÁREA DE CONTENIDO PRINCIPAL (SHADCN TOPBAR & VIEWPORT) ──────────── */}
      <main className="erp-main-viewport">
        {/* Topbar Ejecutiva */}
        <header className="erp-topbar">
          <div className="erp-context-breadcrumb">
            <span className="module-root">LOGIMKT ERP</span>
            <span className="separator">/</span>
            <span className="current-page">
              {activeTab === 'kpis' && 'Indicadores Logísticos y Semáforos'}
              {activeTab === 'wms' && 'Gestión de Almacén, Pick Lists y Stock'}
              {activeTab === 'rma' && 'Peritaje Técnico de Devoluciones'}
              {activeTab === 'match' && 'Conciliación de Compras y Cuentas por Pagar'}
              {activeTab === 'proyeccion' && 'Proyección Financiera y GMV Escalable'}
              {activeTab === 'contabilidad' && 'Libro Diario, Mayor y Catálogo Fiscal'}
            </span>
          </div>
          <div className="erp-topbar-pills">
            <span className="shadcn-badge shadcn-badge-success">
              <CheckCircle2 size={12} />
              IVA 13% El Salvador
            </span>
            <span className="shadcn-badge shadcn-badge-default">
              <DollarSign size={12} />
              Split 90/10 Conforme
            </span>
          </div>
        </header>

        <div className="erp-view-content">
          {/* VISTA 1: KPIS EJECUTIVOS */}
          {/* VISTA 1: KPIS EJECUTIVOS (EFFORD DASHBOARD BLOCK #1 & #2 PATTERN) */}
          {activeTab === 'kpis' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              <div className="erp-section-header">
                <div>
                  <h1 style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <BarChart3 size={24} color="#3B82F6" />
                    Tablero de Mando Logístico (8 KPIs Oficiales)
                  </h1>
                  <p>Métricas operacionales y de cumplimiento fiscal según especificaciones de la cátedra Ing. Pedro Castillo Flores.</p>
                </div>
                <div style={{ display: 'flex', gap: '0.5rem' }}>
                  <span className="shadcn-badge shadcn-badge-default">Corte Mensual</span>
                  <span className="shadcn-badge shadcn-badge-success">Auditado NIIF</span>
                </div>
              </div>

              <div className="erp-kpi-grid">
                {kpis.map((kpi, idx) => {
                  const s = kpi.semaforo.toLowerCase();
                  const colorClass = s === 'verde' ? 'green' : s === 'amarillo' ? 'amber' : 'red';
                  const pct = Math.min(100, Math.max(10, kpi.valor > 100 ? 100 : (kpi.valor / (kpi.unidad === '%' ? 100 : 50)) * 100));
                  return (
                    <div key={idx} className="efferd-kpi-card">
                      <div className={`efferd-kpi-bar ${colorClass}`}></div>
                      <div>
                        <div className="stat-card-header">
                          <span className="stat-name">{kpi.nombre}</span>
                          <span className={`shadcn-badge ${colorClass === 'green' ? 'shadcn-badge-success' : colorClass === 'amber' ? 'shadcn-badge-warning' : 'shadcn-badge-danger'}`}>
                            {colorClass === 'green' && <CheckCircle2 size={11} />}
                            {colorClass === 'amber' && <Clock size={11} />}
                            {colorClass === 'red' && <AlertCircle size={11} />}
                            {kpi.semaforo}
                          </span>
                        </div>
                        <div className="stat-value-group">
                          <span className="stat-value">{kpi.valor}</span>
                          <span className="stat-unit">{kpi.unidad}</span>
                        </div>
                        {/* Micro barra de progreso visual de Efferd */}
                        <div className="efferd-progress-track">
                          <div 
                            className={`efferd-progress-fill ${colorClass}`} 
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      </div>
                      <div className="stat-footer" style={{ marginTop: '0.85rem' }}>
                        <span>Meta: <strong style={{ color: '#F1F5F9' }}>{kpi.meta}</strong></span>
                        <span style={{ maxWidth: '60%', textAlign: 'right' }}>{kpi.descripcion}</span>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* VISTA 2: WMS & FULFILLMENT */}
          {activeTab === 'wms' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              <div className="erp-section-header">
                <div>
                  <h1 style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <Warehouse size={24} color="#3B82F6" />
                    Torre de Control de Fulfillment y Almacén
                  </h1>
                  <p>Orquestación de pedidos, cálculo de ruta óptima S-Shape y catálogo de SKUs en bodega central.</p>
                </div>
                <button 
                  className="erp-btn erp-btn-primary"
                  onClick={handleSimularOrden}
                  disabled={simulacionActiva}
                >
                  <Sparkles size={16} />
                  {simulacionActiva ? 'Generando en WMS...' : '+ Simular Pedido en Caliente'}
                </button>
              </div>

              <div className="efferd-card">
                <div className="efferd-card-header">
                  <div className="efferd-card-title">
                    <Boxes size={18} color="#3B82F6" />
                    Listas de Recolección (Pick Lists) en Bodega
                  </div>
                  <span className="shadcn-badge shadcn-badge-outline">{pickLists.length} Activas</span>
                </div>
                <div className="efferd-card-body" style={{ padding: 0 }}>
                  <div className="erp-table-wrapper" style={{ border: 'none', borderRadius: 0 }}>
                    <table className="erp-data-table">
                      <thead>
                        <tr>
                          <th>Código Pick List</th>
                          <th>Orden Referencia</th>
                          <th>Operador Asignado</th>
                          <th>Ruta en Bodega</th>
                          <th>Avance</th>
                          <th>Estado</th>
                          <th>Acción</th>
                        </tr>
                      </thead>
                      <tbody>
                        {pickLists.map((item) => (
                          <tr key={item.id}>
                            <td><span className="erp-code">{item.numeroPickList}</span></td>
                            <td style={{ fontWeight: '600' }}>{item.numeroOrden}</td>
                            <td>{item.operador}</td>
                            <td style={{ color: '#60A5FA', fontSize: '0.8rem' }}>{item.ruta}</td>
                            <td>{item.itemsCompletados} de {item.totalItems} ítems</td>
                            <td>
                              <span className={`shadcn-badge ${item.estado === 'Completado' ? 'shadcn-badge-success' : item.estado === 'EnProceso' ? 'shadcn-badge-warning' : 'shadcn-badge-danger'}`}>
                                {item.estado === 'Completado' && <CheckCircle2 size={11} />}
                                {item.estado === 'EnProceso' && <Clock size={11} />}
                                {item.estado}
                              </span>
                            </td>
                            <td>
                              {item.estado !== 'Completado' ? (
                                <button 
                                  className="erp-btn erp-btn-primary erp-btn-sm"
                                  onClick={() => handleCompletarPickList(item.id)}
                                >
                                  Confirmar Recolección
                                </button>
                              ) : (
                                <span style={{ color: 'var(--status-green)', fontSize: '0.8rem', fontWeight: '700', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                  <CheckCircle2 size={14} /> Empacado
                                </span>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>

              <div className="efferd-card">
                <div className="efferd-card-header">
                  <div className="efferd-card-title">
                    <Layers size={18} color="#3B82F6" />
                    Mapa de Ubicaciones WMS (Catálogo de Productos)
                  </div>
                  <div style={{ position: 'relative', width: '320px' }}>
                    <Search size={15} style={{ position: 'absolute', left: '10px', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-dim)' }} />
                    <input 
                      type="text"
                      placeholder="Filtrar por SKU, nombre..."
                      value={filtroSku}
                      onChange={(e) => setFiltroSku(e.target.value)}
                      className="erp-search-input"
                      style={{ width: '100%', paddingLeft: '32px' }}
                    />
                  </div>
                </div>
                <div className="efferd-card-body" style={{ padding: 0 }}>
                  <div className="erp-table-wrapper" style={{ border: 'none', borderRadius: 0 }}>
                    <table className="erp-data-table">
                      <thead>
                        <tr>
                          <th>SKU</th>
                          <th>Nombre del Producto</th>
                          <th>Categoría</th>
                          <th>Precio B2C</th>
                          <th>Precio B2B</th>
                          <th>Stock Actual</th>
                          <th>Ubicación WMS</th>
                        </tr>
                      </thead>
                      <tbody>
                        {productosFiltrados.map((p) => (
                          <tr key={p.sku}>
                            <td><span className="erp-code">{p.sku}</span></td>
                            <td style={{ fontWeight: '600' }}>{p.nombre}</td>
                            <td style={{ color: 'var(--text-muted)' }}>{p.categoria}</td>
                            <td style={{ color: '#60A5FA', fontFamily: 'var(--font-mono)' }}>${p.precioB2C.toFixed(2)}</td>
                            <td style={{ color: 'var(--text-dim)', fontFamily: 'var(--font-mono)' }}>${p.precioB2B.toFixed(2)}</td>
                            <td>
                              <span className={`shadcn-badge ${p.stock <= p.stockMinimo ? 'shadcn-badge-danger' : 'shadcn-badge-success'}`}>
                                {p.stock} unidades
                              </span>
                            </td>
                            <td>
                              <span style={{ background: 'rgba(255,255,255,0.06)', padding: '2px 8px', borderRadius: '4px', fontSize: '0.8rem', fontFamily: 'var(--font-mono)' }}>
                                P{p.pasillo}-R{p.rack}-N{p.nivel}-C{p.posicion}
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* VISTA 3: RMA & LOGÍSTICA INVERSA */}
          {activeTab === 'rma' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              <div className="erp-section-header">
                <div>
                  <h1 style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <RotateCcw size={24} color="#3B82F6" />
                    Mesa Técnica de RMA y Logística Inversa
                  </h1>
                  <p>Inspección pericial, triage de mercadería retornada e imputación contable automática.</p>
                </div>
                <span className="shadcn-badge shadcn-badge-warning">Triage Obligatorio</span>
              </div>

              <div className="efferd-card">
                <div className="efferd-card-body" style={{ padding: 0 }}>
                  <div className="erp-table-wrapper" style={{ border: 'none', borderRadius: 0 }}>
                    <table className="erp-data-table">
                      <thead>
                        <tr>
                          <th>N° RMA</th>
                          <th>Orden</th>
                          <th>Producto</th>
                          <th>Motivo Reportado</th>
                          <th>Estado Inspección</th>
                          <th>Dictamen</th>
                          <th>Impacto Contable / Almacén</th>
                          <th>Resolución Técnica</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rmas.map((r) => (
                          <tr key={r.id}>
                            <td><span className="erp-code">{r.numeroRma}</span></td>
                            <td style={{ fontWeight: '600' }}>{r.numeroOrden}</td>
                            <td>
                              <div style={{ fontWeight: '600' }}>{r.producto}</div>
                              <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>{r.sku} (x{r.cantidad})</div>
                            </td>
                            <td style={{ fontSize: '0.82rem', color: 'var(--text-muted)' }}>{r.motivoCliente}</td>
                            <td>
                              <span className={`shadcn-badge ${r.estado === 'Dictaminado' ? 'shadcn-badge-success' : 'shadcn-badge-warning'}`}>
                                {r.estado}
                              </span>
                            </td>
                            <td style={{ fontWeight: '700' }}>
                              <span className={`shadcn-badge ${r.clasificacion === 'ReStock' ? 'shadcn-badge-success' : r.clasificacion === 'MermaScrap' ? 'shadcn-badge-danger' : r.clasificacion === 'Remate' ? 'shadcn-badge-warning' : 'shadcn-badge-outline'}`}>
                                {r.clasificacion}
                              </span>
                            </td>
                            <td style={{ fontSize: '0.82rem', color: '#93C5FD' }}>{r.impactoContable}</td>
                            <td>
                              {r.estado !== 'Dictaminado' ? (
                                <div style={{ display: 'flex', gap: '6px' }}>
                                  <button 
                                    className="erp-btn erp-btn-success erp-btn-sm"
                                    onClick={() => handleDictaminarRma(r.id, 'ReStock')}
                                  >
                                    ReStock
                                  </button>
                                  <button 
                                    className="erp-btn erp-btn-amber erp-btn-sm"
                                    onClick={() => handleDictaminarRma(r.id, 'Remate')}
                                  >
                                    Remate
                                  </button>
                                  <button 
                                    className="erp-btn erp-btn-danger erp-btn-sm"
                                    onClick={() => handleDictaminarRma(r.id, 'MermaScrap')}
                                  >
                                    Scrap
                                  </button>
                                </div>
                              ) : (
                                <span style={{ color: 'var(--status-green)', fontSize: '0.8rem', fontWeight: '700', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                  <CheckCircle2 size={13} /> Procesado
                                </span>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* VISTA 4: 3-WAY MATCH */}
          {activeTab === 'match' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              <div className="erp-section-header">
                <div>
                  <h1 style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <FileCheck2 size={24} color="#3B82F6" />
                    Auditoría Tridireccional (3-Way Matching)
                  </h1>
                  <p>Validación cruzada entre Orden de Compra (PO), Conteo WMS y Factura de Proveedor para autorizar Cuentas por Pagar.</p>
                </div>
                <span className="shadcn-badge shadcn-badge-default">Conciliación Automática</span>
              </div>

              <div className="efferd-card">
                <div className="efferd-card-body" style={{ padding: 0 }}>
                  <div className="erp-table-wrapper" style={{ border: 'none', borderRadius: 0 }}>
                    <table className="erp-data-table">
                      <thead>
                        <tr>
                          <th>N° Orden Compra</th>
                          <th>Proveedor</th>
                          <th>PO Pactada</th>
                          <th>Recepción WMS</th>
                          <th>Factura Proveedor</th>
                          <th>Discrepancia</th>
                          <th>Estado Auditoría</th>
                          <th>Autorización CxP</th>
                        </tr>
                      </thead>
                      <tbody>
                        {matches.map((m) => (
                          <tr key={m.id}>
                            <td><span className="erp-code">{m.numeroPo}</span></td>
                            <td style={{ fontWeight: '600' }}>{m.proveedor}</td>
                            <td>
                              <div>{m.poCantidad} un.</div>
                              <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)', fontFamily: 'var(--font-mono)' }}>${m.poPrecio.toFixed(2)} c/u</div>
                            </td>
                            <td>
                              <span style={{ fontWeight: '700', color: m.wmsCantidad !== m.poCantidad ? 'var(--status-red)' : 'var(--status-green)' }}>
                                {m.wmsCantidad} recibidas
                              </span>
                            </td>
                            <td>
                              <div>{m.facturaCantidad} un.</div>
                              <div style={{ fontSize: '0.75rem', color: m.facturaPrecio !== m.poPrecio ? 'var(--status-red)' : 'var(--text-dim)', fontFamily: 'var(--font-mono)' }}>
                                ${m.facturaPrecio.toFixed(2)} c/u
                              </div>
                            </td>
                            <td>
                              <span className={`shadcn-badge ${m.discrepancia === 'Ninguna' ? 'shadcn-badge-success' : 'shadcn-badge-danger'}`}>
                                {m.discrepancia}
                              </span>
                            </td>
                            <td>
                              <span className={`shadcn-badge ${m.estado === 'Conforme' || m.estado === 'AprobadoManual' ? 'shadcn-badge-success' : 'shadcn-badge-warning'}`}>
                                {m.estado}
                              </span>
                            </td>
                            <td>
                              {m.autorizadoPago ? (
                                <span style={{ color: 'var(--status-green)', fontWeight: '700', fontSize: '0.8rem', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                  <CheckCircle2 size={14} /> Pago Autorizado
                                </span>
                              ) : (
                                <button 
                                  className="erp-btn erp-btn-amber erp-btn-sm"
                                  onClick={() => handleAprobarMatchManual(m.id)}
                                >
                                  Aprobar Manualmente
                                </button>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* VISTA 5: MODELO FINANCIERO */}
          {activeTab === 'proyeccion' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              <div className="erp-section-header">
                <div>
                  <h1 style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <TrendingUp size={24} color="#3B82F6" />
                    Modelo Financiero Escalable a 12 Meses
                  </h1>
                  <p>Proyección volumétrica de GMV, comisiones de plataforma (10%), costos de fulfillment y margen neto.</p>
                </div>
                <button 
                  className="erp-btn erp-btn-primary"
                  onClick={() => window.open('/api/kpis/dashboard/proyeccion-financiera', '_blank')}
                >
                  <ArrowUpRight size={16} />
                  Exportar Modelo a Excel/JSON
                </button>
              </div>

              <div className="efferd-card">
                <div className="efferd-card-body" style={{ padding: 0 }}>
                  <div className="erp-table-wrapper" style={{ border: 'none', borderRadius: 0 }}>
                    <table className="erp-data-table">
                      <thead>
                        <tr>
                          <th>Periodo</th>
                          <th>Órdenes Estimadas</th>
                          <th>GMV Transaccionado ($)</th>
                          <th>Comisión Plataforma (10%)</th>
                          <th>Costo Fulfillment ($)</th>
                          <th>Margen Neto ($)</th>
                        </tr>
                      </thead>
                      <tbody>
                        {mockProyeccion.map((row) => (
                          <tr key={row.mes}>
                            <td style={{ fontWeight: '700' }}>{row.nombreMes}</td>
                            <td>{row.ordenesProyectadas.toLocaleString()} pedidos</td>
                            <td style={{ color: '#60A5FA', fontFamily: 'var(--font-mono)' }}>${row.gmvProyectado.toLocaleString()}</td>
                            <td style={{ color: 'var(--status-green)', fontFamily: 'var(--font-mono)' }}>${row.ingresosComisionPlataforma.toLocaleString()}</td>
                            <td style={{ color: 'var(--status-red)', fontFamily: 'var(--font-mono)' }}>-${row.costoOperativoFulfillment.toLocaleString()}</td>
                            <td style={{ fontWeight: '800', fontFamily: 'var(--font-mono)', color: '#fff' }}>
                              ${row.margenOperativoNeto.toLocaleString()}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* VISTA 6: CONTABILIDAD */}
          {activeTab === 'contabilidad' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              <div className="erp-section-header">
                <div>
                  <h1 style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <Scale size={24} color="#3B82F6" />
                    Mapeo de Partida Doble Automatizada (El Salvador)
                  </h1>
                  <p>Plan de cuentas contable oficial salvadoreño y reglas de cuadratura estricta (Debe == Haber).</p>
                </div>
                <span className="shadcn-badge shadcn-badge-success">NIIF para PYMES</span>
              </div>

              <div className="efferd-card">
                <div className="efferd-card-body" style={{ padding: 0 }}>
                  <div className="erp-table-wrapper" style={{ border: 'none', borderRadius: 0 }}>
                    <table className="erp-data-table">
                      <thead>
                        <tr>
                          <th>Código Cuenta</th>
                          <th>Nombre de Cuenta Contable</th>
                          <th>Naturaleza</th>
                          <th>Evento Transaccional Disparador</th>
                          <th>Regla Operativa en el Sistema</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr>
                          <td><span className="erp-code">1101</span></td>
                          <td style={{ fontWeight: '600' }}>Caja y Bancos</td>
                          <td><span className="shadcn-badge shadcn-badge-default">Deudora</span></td>
                          <td>Cobro a Cliente / Pago a Vendedor</td>
                          <td>Entrada por venta aprobada o egreso por liquidación Split 90/10</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">1108</span></td>
                          <td style={{ fontWeight: '600' }}>Inventario de Mercaderías</td>
                          <td><span className="shadcn-badge shadcn-badge-default">Deudora</span></td>
                          <td>3-Way Match Conforme / RMA ReStock</td>
                          <td>Carga por recepción física validada contra PO y Factura de Proveedor</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">1109</span></td>
                          <td style={{ fontWeight: '600' }}>IVA Crédito Fiscal (13%)</td>
                          <td><span className="shadcn-badge shadcn-badge-default">Deudora</span></td>
                          <td>Compra a Proveedor con CCF</td>
                          <td>Crédito fiscal deducible en compras de suministros e insumos</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">2101</span></td>
                          <td style={{ fontWeight: '600' }}>Cuentas por Pagar Proveedores</td>
                          <td><span className="shadcn-badge shadcn-badge-warning">Acreedora</span></td>
                          <td>3-Way Match Aprobado</td>
                          <td>Obligación comercial según el plazo de crédito (DPO) pactado</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">2105</span></td>
                          <td style={{ fontWeight: '600' }}>IVA Débito Fiscal (13%)</td>
                          <td><span className="shadcn-badge shadcn-badge-warning">Acreedora</span></td>
                          <td>Venta Facturada (B2C / B2B)</td>
                          <td>Impuesto retenido en ventas en el marketplace para reporte mensual</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">2108</span></td>
                          <td style={{ fontWeight: '600' }}>Proveedores Marketplace por Liquidar (90%)</td>
                          <td><span className="shadcn-badge shadcn-badge-warning">Acreedora</span></td>
                          <td>Orden Cobrada con Éxito</td>
                          <td>Fondos en custodia pasiva pendientes de transferencia al vendedor</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">4101</span></td>
                          <td style={{ fontWeight: '600' }}>Ingresos por Comisión Marketplace (10%)</td>
                          <td><span className="shadcn-badge shadcn-badge-success">Acreedora</span></td>
                          <td>Orden Entregada Conforme</td>
                          <td>Ingreso neto operativo de LOGIMKT S.A. reconocido contablemente</td>
                        </tr>
                        <tr>
                          <td><span className="erp-code">5101</span></td>
                          <td style={{ fontWeight: '600' }}>Pérdida Operativa por Merma/Scrap</td>
                          <td><span className="shadcn-badge shadcn-badge-danger">Deudora</span></td>
                          <td>RMA Dictaminado Scrap</td>
                          <td>Gasto extraordinario imputado por daño irreparable en producto</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

export default App;
