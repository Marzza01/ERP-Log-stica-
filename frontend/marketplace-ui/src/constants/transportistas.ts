/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Tarifas y catálogo de Operadores Logísticos / Transportistas en El Salvador (USD)
 */

export interface OperadorLogistico {
  id: string;
  nombre: string;
  tarifaEnvio: number;
  descripcion: string;
  tiempoEstimado: string;
}

export const OPERADORES_LOGISTICOS: OperadorLogistico[] = [
  {
    id: 'C807 Express El Salvador',
    nombre: 'C807 Express El Salvador (Operador Local 3PL)',
    tarifaEnvio: 4.50,
    descripcion: 'Courier local consolidado 3PL',
    tiempoEstimado: '24 - 48 horas',
  },
  {
    id: 'LOGIMKT Express',
    nombre: 'LOGIMKT Express (Flota Local Propia AMSS)',
    tarifaEnvio: 5.00,
    descripcion: 'Entrega prioritaria mismo día en AMSS',
    tiempoEstimado: 'Mismo día (Express)',
  },
  {
    id: 'Urbano Express El Salvador',
    nombre: 'Urbano Express El Salvador',
    tarifaEnvio: 4.00,
    descripcion: 'Paquetería urbana y departamental',
    tiempoEstimado: '24 - 48 horas',
  },
  {
    id: 'Cargo Expreso SV',
    nombre: 'Cargo Expreso El Salvador',
    tarifaEnvio: 4.25,
    descripcion: 'Cobertura nacional departamento a departamento',
    tiempoEstimado: '24 - 72 horas',
  },
  {
    id: 'DHL Express El Salvador',
    nombre: 'DHL Express (Nacional / Internacional)',
    tarifaEnvio: 12.50,
    descripcion: 'Envío express prioritario certificado',
    tiempoEstimado: '24 horas',
  },
  {
    id: 'RetiroEnBodega',
    nombre: 'Retiro en Bodega Central (Pick-Up)',
    tarifaEnvio: 0.00,
    descripcion: 'Retiro sin costo en sucursal o bodega central',
    tiempoEstimado: 'Inmediato',
  },
];

/**
 * Obtener la tarifa automática de envío según el transportista seleccionado.
 */
export function obtenerTarifaTransportista(nombreTransportista: string): number {
  if (!nombreTransportista) return 5.00;

  const normalizado = nombreTransportista.trim().toLowerCase();

  // Coincidencia directa por id o nombre
  const encontrado = OPERADORES_LOGISTICOS.find(
    op => op.id.toLowerCase() === normalizado || op.nombre.toLowerCase() === normalizado
  );
  if (encontrado) return encontrado.tarifaEnvio;

  // Fallbacks por palabras clave
  if (normalizado.includes('c807')) return 4.50;
  if (normalizado.includes('urbano')) return 4.00;
  if (normalizado.includes('cargo')) return 4.25;
  if (normalizado.includes('dhl')) return 12.50;
  if (normalizado.includes('retiro') || normalizado.includes('bodega')) return 0.00;
  if (normalizado.includes('express') || normalizado.includes('logimkt') || normalizado.includes('flota')) return 5.00;

  return 4.50;
}
