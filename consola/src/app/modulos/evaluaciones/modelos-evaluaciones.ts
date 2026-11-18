export type EstadoValidacion = 'VALIDADA' | 'RECHAZADA' | 'PENDIENTE';
export type TipoConflicto = 'VERSION' | 'DUPLICADO';
export type EstadoConflicto = 'PENDIENTE' | 'RESUELTO';
export type DecisionConflicto = 'PREVALECE_ENTRANTE' | 'PREVALECE_VIGENTE' | 'FUSIONAR' | 'RECHAZAR_DUPLICADO';
export type EleccionVigente = 'VIGENTE' | 'ENTRANTE';

export interface VersionEvaluacion {
  versionId: string;
  evaluacionId: string;
  numeroVersion: number;
  autor: string;
  idDispositivo: string;
  versionActivoBase?: number | null;
  enConflicto: boolean;
  instante: string;
  contenido: Record<string, unknown>;
  motivo: string;
  detalle?: string | null;
}

/** El servicio omite los campos nulos: un conflicto pendiente no trae decisión. */
export interface Conflicto {
  id: string;
  activoId: string;
  tipo: TipoConflicto;
  estado: EstadoConflicto;
  regla: string;
  instante: string;
  vigente?: VersionEvaluacion | null;
  entrante: VersionEvaluacion;
  decision?: DecisionConflicto | null;
  decididoPor?: string | null;
  justificacion?: string | null;
  instanteResolucion?: string | null;
}

export interface SolicitudResolucion {
  decision: DecisionConflicto;
  justificacion: string;
  vigente?: EleccionVigente;
}

export interface CriterioTipo {
  nivel: string;
  criterio: string;
}

export interface TipoActivo {
  codigo: string;
  nombre: string;
  criterios: CriterioTipo[];
  version?: number;
  activo?: boolean;
}
