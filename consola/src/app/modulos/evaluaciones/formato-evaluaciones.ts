import { NOMBRE_NIVEL, legible, nombreOrigen } from '../mapa/formato-mapa';
import { NivelDano } from '../mapa/modelos-mapa';
import { Conflicto, DecisionConflicto, VersionEvaluacion } from './modelos-evaluaciones';

const fecha = new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'medium' });
export const fechaExacta = (valor: string): string => {
  const instante = new Date(valor);
  return Number.isNaN(instante.getTime()) ? valor : fecha.format(instante);
};

export interface OpcionDecision {
  valor: DecisionConflicto;
  nombre: string;
  ayuda: string;
}

export const DECISIONES: readonly OpcionDecision[] = [
  { valor: 'PREVALECE_ENTRANTE', nombre: 'Prevalece la entrante', ayuda: 'La versión que llegó en conflicto pasa a ser la vigente del activo.' },
  { valor: 'PREVALECE_VIGENTE', nombre: 'Prevalece la vigente', ayuda: 'El activo conserva su versión actual; la entrante queda en el historial.' },
  { valor: 'FUSIONAR', nombre: 'Fusionar', ayuda: 'Es el mismo activo: una evaluación queda vigente y la otra se marca como duplicado.' },
  { valor: 'RECHAZAR_DUPLICADO', nombre: 'No es duplicado', ayuda: 'Son activos distintos: los dos se conservan.' },
];

export const nombreDecision = (decision: DecisionConflicto): string => DECISIONES.find((d) => d.valor === decision)?.nombre ?? legible(decision);

/** Qué admite el servicio: una versión se elige, un duplicado se fusiona o se descarta, y sin vigente solo cabe la entrante. */
export function razonNoDisponible(conflicto: Conflicto, decision: DecisionConflicto): string | null {
  const eligeVersion = decision === 'PREVALECE_ENTRANTE' || decision === 'PREVALECE_VIGENTE';
  if (conflicto.tipo === 'VERSION' && !eligeVersion) {
    return 'No aplica a un conflicto de versión.';
  }
  if (conflicto.tipo === 'DUPLICADO' && eligeVersion) {
    return 'No aplica a un posible duplicado.';
  }
  if (!conflicto.vigente && (decision === 'PREVALECE_VIGENTE' || decision === 'FUSIONAR')) {
    return 'No hay una versión vigente.';
  }
  return null;
}

const ETIQUETAS: Record<string, string> = {
  nivelDano: 'Nivel de daño',
  tipoActivoCodigo: 'Tipo de activo',
  activoNuevo: 'Referencia del activo',
  descripcion: 'Descripción',
  evaluador: 'Evaluador',
  organizacion: 'Organización',
  instanteCampo: 'Instante en campo',
  lat: 'Latitud',
  lon: 'Longitud',
  precisionM: 'Precisión (m)',
  evidencias: 'Evidencias',
  desastreId: 'Desastre',
  activoId: 'Activo',
  id: 'Evaluación',
};

function textoCampo(clave: string, valor: unknown): string {
  if (valor === undefined || valor === null || valor === '') {
    return 'Sin dato';
  }
  switch (clave) {
    case 'nivelDano':
      return NOMBRE_NIVEL[valor as NivelDano] ?? String(valor);
    case 'tipoActivoCodigo':
      return legible(String(valor));
    case 'instanteCampo':
      return fechaExacta(String(valor));
    case 'evidencias':
      return String(Array.isArray(valor) ? valor.length : 0);
    case 'activoNuevo':
      return String((valor as { referencia?: unknown }).referencia ?? 'Activo nuevo');
    default:
      return typeof valor === 'object' ? JSON.stringify(valor) : String(valor);
  }
}

export interface FilaComparada {
  etiqueta: string;
  vigente: string;
  entrante: string;
  distinto: boolean;
}

function fila(etiqueta: string, vigente: string, entrante: string): FilaComparada {
  return { etiqueta, vigente, entrante, distinto: vigente !== entrante };
}

/** Las dos versiones completas, campo por campo: primero su procedencia y después todo su contenido. */
export function compararVersiones(conflicto: Conflicto): FilaComparada[] {
  const sinVersion = 'Sin versión vigente';
  const { vigente: v, entrante: e } = conflicto;
  const de = (leer: (x: VersionEvaluacion) => string) => (v ? leer(v) : sinVersion);
  const filas = [
    fila('Versión', de((x) => String(x.numeroVersion)), String(e.numeroVersion)),
    fila('Registrada', de((x) => fechaExacta(x.instante)), fechaExacta(e.instante)),
    fila('Autor', de((x) => x.autor), e.autor),
    fila('Dispositivo', de((x) => x.idDispositivo), e.idDispositivo),
    fila('Origen', de((x) => nombreOrigen(x.motivo)), nombreOrigen(e.motivo)),
  ];
  const conocidas = Object.keys(ETIQUETAS);
  const claves = new Set([...Object.keys(v?.contenido ?? {}), ...Object.keys(e.contenido)]);
  const ordenadas = [...conocidas.filter((c) => claves.has(c)), ...[...claves].filter((c) => !conocidas.includes(c)).sort()];
  for (const clave of ordenadas) {
    filas.push(fila(ETIQUETAS[clave] ?? legible(clave), de((x) => textoCampo(clave, x.contenido[clave])), textoCampo(clave, e.contenido[clave])));
  }
  return filas;
}
