export type Rol = 'Autoridad' | 'Responsable' | 'Auditor';
export type Estado = 'Formulado' | 'Aprobado' | 'EnEjecucion' | 'Finalizado' | 'Cancelado';

export interface Perfil {
  usuario: string;
  nombre: string;
  rol: Rol;
  ambito?: 'Local' | 'Regional' | 'Nacional';
}

export interface Desastre { id: string; nombre: string; estado: string; enReconstruccion: boolean; }

export interface Candidata {
  idEvaluacion: string; idActivo: string; nombreActivo: string; tipoActivo: string; nivelDano: string; municipio: string;
}

export interface Proyecto {
  id: string; idDesastre: string; nombre: string; alcance: string; estado: Estado;
  presupuesto: number | null; fuenteFinanciacion: string | null; responsable: string | null; contratista: string | null;
  inicio: string | null; fin: string | null; avance: number; costoEjecutado: number; enSobrecosto: boolean; version: number;
}

export interface EntradaHistorial {
  version: number; tipoCambio: string; valorAnterior: string | null; valorNuevo: string | null;
  autor: string; justificacion: string; instante: string; procedencia: string;
}

export interface EstadoEnFecha {
  estado: string | null; presupuesto: number | null; fuenteFinanciacion: string | null; responsable: string | null;
  contratista: string | null; cronograma: string | null; avance: number; costoEjecutado: number; version: number;
}

export interface Historial { idProyecto: string; fecha: string | null; estadoEnFecha: EstadoEnFecha | null; entradas: EntradaHistorial[]; }

export interface Consolidado {
  idDesastre: string; totalProyectos: number; proyectosPorEstado: Record<Estado, number>;
  presupuestoAsignado: number; costoEjecutado: number; avanceAgregado: number;
  proyectosEnSobrecosto: string[]; todosEnEstadoFinal: boolean;
}

export interface ErrorApi { codigo: string; mensaje: string; detalles?: string[] | null; }

export const NOMBRE_ESTADO: Record<Estado, string> = {
  Formulado: 'Formulado', Aprobado: 'Aprobado', EnEjecucion: 'En ejecución', Finalizado: 'Finalizado', Cancelado: 'Cancelado',
};

export const NOMBRE_DANO: Record<string, string> = {
  SinDano: 'Sin daño', Leve: 'Leve', Moderado: 'Moderado', Grave: 'Grave', DestruccionTotal: 'Destrucción total',
};
