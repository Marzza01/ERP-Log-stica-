/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

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
export interface StockFisicoWms {
  id: string;
  productoId: string;
  cantidadTotal: number;
  cantidadReservada: number;
  cantidadDisponible: number;
  puntoReorden: number;
  ubicacionAlmacenId?: string;
  ubicacionCodigo: string;
  ultimaActualizacion: string;
}

export interface UbicacionAlmacen {
  id: string;
  codigo: string;
  pasillo: string;
  rack: string;
  nivel: number;
  posicion: string;
  capacidadMaxima: number;
  estaOcupada: boolean;
  zona: string;
  fechaCreacion: string;
  stocksAlmacenados?: StockFisicoWms[];
}

export interface MapaPasillo {
  pasillo: string;
  totalRacks: number;
  totalPosiciones: number;
  posicionesOcupadas: number;
  ubicaciones: UbicacionAlmacen[];
}

export interface ResumenBodega {
  totalUbicaciones: number;
  ocupadas: number;
  disponibles: number;
  porcentajeOcupacion: number;
  pasillos: MapaPasillo[];
}

// ── FULFILLMENT ───────────────────────────────────────────────────────────────
export type EstadoPickList = 'Pendiente' | 'EnProceso' | 'Completado' | 'Cancelado';

export interface ItemPickList {
  id: string;
  pickListId?: string;
  productoId?: string;
  sku: string;
  nombreProducto: string;
  cantidadSolicitada: number;
  cantidadRecogida: number;
  pasillo: string;
  rack: string;
  nivel: number;
  posicion?: string;
  ubicacionCodigo: string;
  recogido?: boolean;
  orden?: number;
}

export interface PickList {
  id: string;
  numeroPickList: string;
  ordenId: string;
  numeroOrden: string;
  estado: EstadoPickList;
  operadorAsignado: string;
  fechaCreacion: string;
  fechaInicio?: string;
  fechaFin?: string;
  items: ItemPickList[];
}

// ── PAQUETE (FULFILLMENT) ─────────────────────────────────────────────────────
export type EstadoPaquete = 'Pendiente' | 'Empacando' | 'ListoParaDespacho' | 'Despachado';

export interface Paquete {
  id: string;
  numeroGuia: string;
  ordenId?: string;
  numeroOrden: string;
  estado: EstadoPaquete;
  pesoRealKg: number;
  largoCm?: number;
  anchoCm?: number;
  altoCm?: number;
  volumenM3: number;
  transportista: string;
  departamentoDestino: string;
  municipioDestino: string;
  direccionEntrega?: string;
  contactoReceptor?: string;
  telefonoReceptor?: string;
  fechaEmpaque: string;
  fechaDespacho?: string;
}

// ── RMA (DEVOLUCIONES) ────────────────────────────────────────────────────────
export type EstadoRMA =
  | 'Solicitado'
  | 'RecibidoEnBodega'
  | 'EnInspeccion'
  | 'Dictaminado'
  | 'Completado'
  | 'Rechazado';

export type ClasificacionRMA = 'Pendiente' | 'ReStock' | 'MermaScrap' | 'Remate';

export interface DevolucionRMA {
  id: string;
  numeroRMA: string;
  ordenId: string;
  productoId: string;
  cantidad: number;
  motivoCliente: string;
  estado: EstadoRMA;
  clasificacion: ClasificacionRMA;
  inspectorTecnico?: string;
  dictamenTecnico?: string;
  porcentajeRemateDescuento: number;
  fechaSolicitud: string;
  fechaInspeccion?: string;
}

// ── COMPRAS / 3-WAY MATCH ────────────────────────────────────────────────────
// Modelo de vista. La capa api/compras.ts adapta los DTO reales de Finance.
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
  id: string;
  productoId: string;
  cantidadTotal: number;
  cantidadReservada: number;
  cantidadDisponible: number;
  puntoReorden: number;
  esBajoMinimo: boolean;
  ubicacionAlmacenId?: string;
  ubicacionCodigo: string;
  ultimaActualizacion: string;
}

export interface Producto {
  id: string;
  sku: string;
  nombre: string;
  categoriaNombre: string;
  subcategoria: string;
  precioB2C: number;
  precioB2B: number;
  precioBase: number;
  moneda: string;
  stockInicial: number;
  stockMinimo: number;
  proveedorNombre: string;
  paisOrigen: string;
  pesoKg: number;
  dimensionesCm: string;
  unidadMedida: string;
  estadoProducto: string;
  codigoEAN: string;
  descripcion: string;
  vendedorId: string;
  categoriaId: string;
  estaPublicado: boolean;
  fechaCreacion: string;
  fechaPublicacion?: string;
  stock?: StockFisico;
}

// ── PAGINACIÓN GENÉRICA ───────────────────────────────────────────────────────
export interface PaginatedResponse<T> {
  total: number;
  pagina: number;
  tamano: number;
  data: T[];
}
