import type { Feature, Geometry, Point } from 'geojson';

export const NIVELES = ['SIN_DANO', 'LEVE', 'MODERADO', 'GRAVE', 'DESTRUCCION_TOTAL'] as const;
export type NivelDano = (typeof NIVELES)[number];
export type Distribucion = Record<NivelDano, number>;

// ---------------------------------------------------------------- Consulta de Daños

export interface PropiedadesZonaDano {
  tipo: 'zona';
  codigoMunicipio: string;
  nombre: string | null;
  codigoDepartamento: string | null;
  activos: number;
  indiceDano: number | null;
  distribucion: Distribucion;
  conConflicto: number;
}

export interface PropiedadesActivo {
  tipo: 'activo';
  idActivo: string;
  idEvaluacion: string;
  tipoActivo: string;
  nivelDano: NivelDano;
  peso: number;
  validada: boolean;
  estadoValidacion: string | null;
  conflictoPendiente: boolean;
  codigoMunicipio: string;
  datoAl: string;
}

export type ZonaDano = Feature<Geometry | null, PropiedadesZonaDano>;
export type ActivoMapa = Feature<Point, PropiedadesActivo>;

export interface MapaDanos {
  type: 'FeatureCollection';
  idDesastre: string;
  datoAl: string | null;
  degradado: boolean;
  resumen: { activos: number; indiceDano: number | null; distribucion: Distribucion; conConflicto: number };
  features: (ZonaDano | ActivoMapa)[];
}

export interface FiltroDanos {
  desastre: string;
  nivelMinimo: NivelDano;
  soloValidadas: boolean;
}

export interface EvaluacionActivo {
  instante: string;
  idEvaluacion: string;
  versionEvaluacion: number;
  origen: string;
  nivelDano: NivelDano;
  tipoActivo: string;
  estadoValidacion: string | null;
  enConflicto: boolean;
  codigoMunicipio: string;
  autor: string | null;
  detalle: string | null;
}

export interface EvolucionActivo {
  idActivo: string;
  datoAl: string | null;
  vigente: {
    tipoActivo: string | null;
    nivelDano: NivelDano | null;
    codigoMunicipio: string | null;
    estadoValidacion: string | null;
    validada: boolean;
    conflictoPendiente: boolean;
    fusionado: boolean;
  };
  evaluaciones: EvaluacionActivo[];
}

// ---------------------------------------------------------------- Consulta (desastres)

export interface FilaDesastre {
  id: string;
  tipo: string | null;
  nombre: string | null;
  severidad: string | null;
  estado: string | null;
  zonaVersion: number | null;
  municipios: number;
  datoAl: string | null;
}

export interface TableroDesastres {
  datos: FilaDesastre[];
  datoAl: string | null;
  degradado: boolean;
}

export interface PropiedadesZonaDesastre {
  idDesastre: string;
  nombre: string | null;
  severidad: string | null;
  estado: string | null;
  zonaVersion: number;
  vigenteDesde: string;
  municipios: string[];
  datoAl: string | null;
}

export type ZonaDesastre = Feature<Geometry, PropiedadesZonaDesastre>;

export interface MapaZonas {
  type: 'FeatureCollection';
  datoAl: string | null;
  degradado: boolean;
  features: ZonaDesastre[];
}

export interface CambioDesastre {
  instante: string;
  tipo: string;
  anterior: string | null;
  nuevo: string | null;
  autor: string;
  version: number;
}

export interface VersionZona {
  version: number;
  vigenteDesde: string;
  geometriaGeoJson: string | null;
  codigosMunicipio: string[];
  fuente: string;
}

export interface EstadoEnInstante {
  instante: string;
  severidad: string | null;
  estado: string | null;
  zona: VersionZona | null;
}

export interface EvolucionDesastre {
  idDesastre: string;
  cambios: CambioDesastre[];
  zonas: VersionZona[];
  enInstante: EstadoEnInstante | null;
  datoAl: string | null;
}

// ---------------------------------------------------------------- lectura y dibujo

/** Una respuesta y si es la última conocida porque el servicio no pudo armarla (`degradado` o `X-Degradado`). */
export interface Respuesta<T> {
  cuerpo: T;
  degradado: boolean;
}

export type NombreCapa = 'coropleta' | 'activos' | 'zona';

export interface ZonaDibujo {
  version: number;
  geometria: Geometry;
}
