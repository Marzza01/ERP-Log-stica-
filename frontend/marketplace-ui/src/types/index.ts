// ─────────────────────────────────────────────────────────────────────────────
// MarketplaceERP — TypeScript Interfaces
// Mapeadas exactamente a los DTOs del backend ASP.NET Core
// ─────────────────────────────────────────────────────────────────────────────

// ── AUTH ─────────────────────────────────────────────────────────────────────
export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  expira: string;
  usuario: string;
  rol: string;
}

export interface Usuario {
  nombre: string;
  email: string;
  rol: string;
}

// ── ÓRDENES (OMS) ────────────────────────────────────────────────────────────
export type EstadoOrden =
  | 'Pendiente'
  | 'Aprobada'
  | 'EnProceso'
  | 'Enviada'
  | 'Entregada'
  | 'Cancelada';

export type TipoCliente = 'ConsumidorFinal' | 'Empresa' | 'GobiernoPublico';

export interface ItemOrden {
  id: string;
  productoId: string;
  skuReferencia: string;
  nombreProducto: string;
  cantidad: number;
  precioUnitario: number;
}

export interface SplitPago {
  id: string;
  ordenId: string;
  montoTotal: number;
  montoVendedor: number;
  montoMarketplace: number;
  fechaProcesado: string;
}

export interface Orden {
  id: string;
  numeroOrden: string;
  estado: EstadoOrden;
  tipoCliente: TipoCliente;
  montoTotal: number;
  subTotal: number;
  impuestoIva: number;
  costoEnvio: number;
  moneda: string;
  tipoComprobante: string;
  transportista: string;
  guiaEnvio?: string;
  direccionEntrega: string;
  zonaEnvio: string;
  fechaCreacion: string;
  fechaAprobacion?: string;
  fechaEnvio?: string;
  fechaEntregaEstimada?: string;
  fechaEntregaReal?: string;
  totalItems: number;
  minutosEnEstado: number;
  items?: ItemOrden[];
  splitPago?: SplitPago;
}

export interface OrdenesResponse {
  total: number;
  pagina: number;
  tamano: number;
  ordenes: Orden[];
}

export interface CrearOrdenRequest {
  compradorId: string;
  vendedorId: string;
  tipoCliente: TipoCliente;
  tipoComprobante?: string;
  condicionPago?: string;
  transportista?: string;
  zonaEnvio?: string;
  direccionEntrega?: string;
  costoEnvio?: number;
  items: {
    productoId: string;
    sku?: string;
    nombre?: string;
    cantidad: number;
    precioUnitario: number;
  }[];
}

// ── KPIs DASHBOARD ────────────────────────────────────────────────────────────
export type SemaforoColor = 'Verde' | 'Amarillo' | 'Rojo';

export interface Kpi {
  numero: number;
  nombre: string;
  valor: number;
  unidad: string;
  meta: number;
  semaforo: SemaforoColor;
  descripcion: string;
  cumpleMeta: boolean;
}

export interface ResumenEjecutivo {
  totalOrdenes: number;
  ordenesEntregadas: number;
  ordenesCanceladas: number;
  totalMatches3Way: number;
  totalRmasPeriodo: number;
  totalComisionGenerada: number;
}

export interface DashboardKpis {
  periodo: {
    desde: string;
    hasta: string;
    diasPeriodo: number;
  };
  generadoEn: string;
  resumenEjecutivo: ResumenEjecutivo;
  kpis: Kpi[];
}

export interface ProyeccionMes {
  mes: number;
  periodo: string;
  ventasProyectadas: number;
  comisionMarketplace: number;
  costosOperativos: number;
  utilidadNeta: number;
  margenNetoPct: number;
  factorCrecimiento: number;
}

export interface ModeloFinanciero {
  generadoEn: string;
  supuestos: Record<string, string | boolean>;
  resumenAnual: {
    ventasTotalesAnuales: number;
    comisionTotalAnual: number;
    costosTotalesAnuales: number;
    utilidadTotalAnual: number;
  };
  meses: ProyeccionMes[];
}

// ── WMS ───────────────────────────────────────────────────────────────────────
export interface UbicacionAlmacen {
  id: string;
  codigo: string;
  pasillo: string;
  rack: string;
  nivel: string;
  posicion: string;
  capacidadMaxima: number;
  ocupacionActual: number;
  disponible: boolean;
  porcentajeOcupacion: number;
}

export interface ResumenBodega {
  totalUbicaciones: number;
  ubicacionesOcupadas: number;
  ubicacionesDisponibles: number;
  porcentajeOcupacionTotal: number;
  pasillos: string[];
  alertasCapacidad: string[];
}

// ── FULFILLMENT ───────────────────────────────────────────────────────────────
export type EstadoPickList = 'Pendiente' | 'EnProceso' | 'Completada' | 'Cancelada';

export interface ItemPickList {
  id: string;
  sku: string;
  nombreProducto: string;
  cantidadSolicitada: number;
  cantidadRecolectada: number;
  ubicacionCodigo: string;
  pasillo: string;
  rack: string;
  nivel: string;
  orden: number;
}

export interface PickList {
  id: string;
  numeroPickList: string;
  ordenId: string;
  numeroOrden: string;
  operadorAsignado: string;
  estado: EstadoPickList;
  fechaCreacion: string;
  fechaCompletado?: string;
  items: ItemPickList[];
}

// ── PAQUETE (FULFILLMENT) ─────────────────────────────────────────────────────
export type EstadoPaquete = 'Empacado' | 'Despachado' | 'Entregado';

export interface Paquete {
  id: string;
  numeroGuia: string;
  numeroOrden: string;
  transportista: string;
  pesoRealKg: number;
  volumenM3: number | string;
  municipioDestino: string;
  departamentoDestino: string;
  estado: EstadoPaquete;
  fechaEmpaque: string;
  fechaDespacho?: string;
}

// ── RMA (DEVOLUCIONES) ────────────────────────────────────────────────────────
export type EstadoRMA = 'Solicitada' | 'Aprobada' | 'Rechazada' | 'Completada';
export type MotivoRMA =
  | 'ProductoDefectuoso'
  | 'ErrorEnvio'
  | 'NoCorresponde'
  | 'DanoTransporte'
  | 'Otro';

export interface DevolucionRMA {
  id: string;
  ordenId: string;
  motivo: MotivoRMA;
  descripcion: string;
  estado: EstadoRMA;
  fechaSolicitud: string;
  fechaResolucion?: string;
  reembolsoAprobado: number;
}

// ── COMPRAS / 3-WAY MATCH ────────────────────────────────────────────────────
export type EstadoOrdenCompra = 'Borrador' | 'Emitida' | 'Recibida' | 'Cerrada' | 'Cancelada';
export type Estado3WayMatch = 'Conforme' | 'Discrepante' | 'Pendiente';

export interface ItemOrdenCompra {
  id: string;
  productoId: string;
  descripcion: string;
  cantidad: number;
  precioUnitario: number;
  total: number;
}

export interface FacturaProveedor {
  id: string;
  numeroFactura: string;
  montoTotal: number;
  fechaEmision: string;
}

export interface MatchTridireccional {
  id: string;
  ordenCompraId: string;
  resultado: Estado3WayMatch;
  diferenciaCantidad: number;
  diferenciaMonto: number;
  observaciones: string;
  fechaAuditoria: string;
}

export interface OrdenCompra {
  id: string;
  numeroPO: string;
  proveedorId: string;
  nombreProveedor: string;
  estado: EstadoOrdenCompra;
  montoTotal: number;
  moneda: string;
  condicionPago: string;
  fechaEmision: string;
  fechaEntregaEsperada?: string;
  items: ItemOrdenCompra[];
  facturas?: FacturaProveedor[];
  matches3Way?: MatchTridireccional[];
}

// ── CONTABILIDAD ──────────────────────────────────────────────────────────────
export interface LineaPartida {
  id: string;
  cuenta: string;
  descripcion: string;
  debe: number;
  haber: number;
}

export interface PartidaContable {
  id: string;
  numeroAsiento: string;
  fecha: string;
  descripcion: string;
  referencia: string;
  tipoOrigen: string;
  lineas: LineaPartida[];
  totalDebe: number;
  totalHaber: number;
}

// ── LIQUIDACIONES ─────────────────────────────────────────────────────────────
export type EstadoLiquidacion = 'Pendiente' | 'Procesada' | 'Fallida';

export interface Liquidacion {
  id: string;
  vendedorId: string;
  periodo: string;
  montoVentas: number;
  comisionMarketplace: number;
  montoLiquidar: number;
  estado: EstadoLiquidacion;
  fechaCreacion: string;
  fechaProcesado?: string;
}

// ── PRODUCTOS ─────────────────────────────────────────────────────────────────
export interface StockFisico {
  cantidadDisponible: number;
  cantidadReservada: number;
  cantidadTotal: number;
}

export interface Producto {
  id: string;
  sku: string;
  nombre: string;
  descripcion: string;
  precio: number;
  categoria: string;
  marca: string;
  activo: boolean;
  stockFisico?: StockFisico;
}

// ── PAGINACIÓN GENÉRICA ───────────────────────────────────────────────────────
export interface PaginatedResponse<T> {
  total: number;
  pagina: number;
  tamano: number;
  data: T[];
}
