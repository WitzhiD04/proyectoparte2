import type { Geometry } from 'geojson';
import { CambioDesastre, NIVELES, NivelDano, VersionZona } from './modelos-mapa';

/** Una sola rampa de óxido, de claro a oscuro: el daño es una magnitud ordenada, no cinco categorías. */
export const COLOR_NIVEL: Record<NivelDano, string> = {
  SIN_DANO: '#fde6d9',
  LEVE: '#f3a87e',
  MODERADO: '#dd6430',
  GRAVE: '#a23315',
  DESTRUCCION_TOTAL: '#541809',
};

export const NOMBRE_NIVEL: Record<NivelDano, string> = {
  SIN_DANO: 'Sin daño',
  LEVE: 'Leve',
  MODERADO: 'Moderado',
  GRAVE: 'Grave',
  DESTRUCCION_TOTAL: 'Destrucción total',
};

export const COLOR_SIN_DATO = '#c9d2d8';

const NOMBRE_ORIGEN: Record<string, string> = {
  SINCRONIZACION: 'Sincronización',
  ACTUALIZACION: 'Actualización',
  ASOCIACION: 'Asociación',
  RESOLUCION: 'Resolución de conflicto',
  VALIDACION: 'Validación',
};

const NOMBRE_VALIDACION: Record<string, string> = { VALIDADA: 'Validada', RECHAZADA: 'Rechazada', PENDIENTE: 'Pendiente' };

const NOMBRE_ESTADO: Record<string, string> = {
  REGISTRADO: 'Registrado',
  EN_EVALUACION: 'En evaluación',
  EN_ATENCION: 'En atención',
  EN_RECUPERACION: 'En recuperación',
  EN_RECONSTRUCCION: 'En reconstrucción',
  CERRADO: 'Cerrado',
};

const decimal = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 });
const instanteExacto = new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'medium' });

export const numero = (valor: number): string => decimal.format(valor);

/** Con segundos: varios cambios de un desastre pueden caer en el mismo minuto. */
export const fechaExacta = (instante: string | null): string => (instante ? instanteExacto.format(new Date(instante)) : '');

export const cantidad = (n: number, singular: string, plural = `${singular}s`): string => `${n} ${n === 1 ? singular : plural}`;

export const textoZona = (version: number | null, municipios: number): string =>
  version === null ? 'Sin delimitar' : `Versión ${version}, ${cantidad(municipios, 'municipio')}`;

/** Cada índice cae en el nivel de daño cuyo peso (0, 0,25, 0,5, 0,75 o 1) tiene más cerca. */
export function nivelDeIndice(indice: number): NivelDano {
  const posicion = Math.round(Math.min(1, Math.max(0, indice)) * (NIVELES.length - 1));
  return NIVELES[posicion];
}

export const colorIndice = (indice: number | null): string => (indice === null ? COLOR_SIN_DATO : COLOR_NIVEL[nivelDeIndice(indice)]);

export const radioNivel = (nivel: NivelDano): number => 5 + NIVELES.indexOf(nivel);

export const nombreOrigen = (origen: string): string => NOMBRE_ORIGEN[origen] ?? legible(origen);

export const nombreValidacion = (estado: string | null): string => (estado ? (NOMBRE_VALIDACION[estado] ?? legible(estado)) : 'Sin validar');

export const nombreEstado = (estado: string | null): string => (estado ? (NOMBRE_ESTADO[estado] ?? legible(estado)) : 'Sin dato');

export const nombreSeveridad = (severidad: string | null): string => (severidad ? legible(severidad) : 'Sin dato');

/** `ESCUELA_RURAL` se muestra como `Escuela rural`. */
export function legible(codigo: string): string {
  const texto = codigo.replaceAll('_', ' ').toLowerCase();
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

export function textoCambio(cambio: CambioDesastre): string {
  const { tipo, anterior, nuevo } = cambio;
  switch (tipo) {
    case 'REGISTRO': {
      const [severidad, estado] = (nuevo ?? '').split('|');
      return `Registro: ${nombreSeveridad(severidad || null)}, ${nombreEstado(estado || null).toLowerCase()}`;
    }
    case 'SEVERIDAD':
      return `Severidad: ${nombreSeveridad(anterior)} a ${nombreSeveridad(nuevo)}`;
    case 'ESTADO':
      return `Estado: ${nombreEstado(anterior)} a ${nombreEstado(nuevo)}`;
    case 'ZONA':
      return anterior ? `Zona: ${anterior} a ${nuevo}` : `Zona: ${nuevo}`;
    case 'ORGANISMOS':
      return `Organismos: responsable ${nuevo}`;
    default:
      return legible(tipo);
  }
}

/** Los instantes distintos en que cambió el desastre, en orden: son las paradas de la barra de tiempo. */
export function instantesDe(cambios: readonly CambioDesastre[]): string[] {
  const unicos = [...new Set(cambios.map((c) => c.instante))];
  return unicos.sort((a, b) => Date.parse(a) - Date.parse(b));
}

/** La parada en la que entra en vigor una versión de la zona. */
export function paradaDeVersion(instantes: readonly string[], version: VersionZona): number {
  const desde = Date.parse(version.vigenteDesde);
  const parada = instantes.findIndex((i) => Date.parse(i) >= desde);
  return parada === -1 ? instantes.length - 1 : parada;
}

export function geometriaDe(version: VersionZona): Geometry | null {
  if (!version.geometriaGeoJson) {
    return null;
  }
  try {
    return JSON.parse(version.geometriaGeoJson) as Geometry;
  } catch {
    return null;
  }
}
