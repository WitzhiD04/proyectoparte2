namespace Plataforma.Comun.Eventos;

// Enumeraciones canónicas (enmienda E-08). Los nombres se escriben tal cual viajan en el JSON,
// de modo que el contrato y el código usan el mismo texto. Un valor nuevo es un cambio del contrato (R-03).

/// <summary>Categorías de daño (SRS RN-5, enunciado §4.2).</summary>
public enum NivelDano { SIN_DANO, LEVE, MODERADO, GRAVE, DESTRUCCION_TOTAL }

/// <summary>Estados del ciclo de vida de un desastre (enunciado §4.1). Los perfiles por tipo (AD-07) solo usan estos códigos.</summary>
public enum EstadoDesastre { DETECTADO, REGISTRADO, EN_EVALUACION, EN_ATENCION, EN_RECUPERACION, EN_RECONSTRUCCION, CERRADO }

/// <summary>Niveles de clasificación de un desastre (SRS RN-3).</summary>
public enum Severidad { NIVEL_1, NIVEL_2, NIVEL_3, NIVEL_4 }

/// <summary>Origen del registro de un desastre (campo "origen" de EV-03).</summary>
public enum OrigenDesastre { MANUAL, EXTERNO }

/// <summary>Estados de un proyecto de reconstrucción (Documento de diseño de Reconstrucción).</summary>
public enum EstadoProyecto { FORMULADO, APROBADO, EN_EJECUCION, FINALIZADO, CANCELADO }

/// <summary>Territorio que abarca un proyecto. NACIONAL: municipios de varios departamentos (DA-14, pregunta P-02).</summary>
public enum AlcanceProyecto { MUNICIPAL, REGIONAL, NACIONAL }

/// <summary>Tipo de cambio que registra una entrada del historial del proyecto (EV-19 y EV-20).</summary>
public enum TipoCambioProyecto { CREACION, PRESUPUESTO, FUENTE, RESPONSABLE, CRONOGRAMA, CONTRATISTA, ESTADO, AVANCE, COSTO, DOCUMENTO }

/// <summary>Estado de una evaluación de daños. DUPLICADO: descartada al fusionar dos duplicados (E-14).</summary>
public enum EstadoEvaluacion { PRELIMINAR, REGISTRADA, EN_CONFLICTO, VALIDADA, RECHAZADA, DUPLICADO }

/// <summary>Resultado de la validación de una evaluación (SRS RF-ED-08).</summary>
public enum EstadoValidacion { VALIDADA, RECHAZADA, PENDIENTE }

/// <summary>Regla que detectó un conflicto de sincronización.</summary>
public enum TipoConflicto { VERSION, DUPLICADO }

public enum EstadoRevision { PENDIENTE, RESUELTO }

/// <summary>Decisión humana sobre un conflicto o un posible duplicado (AD-32, AD-33, E-05).</summary>
public enum DecisionConflicto { PREVALECE_ENTRANTE, PREVALECE_VIGENTE, FUSIONAR, RECHAZAR_DUPLICADO }

/// <summary>Motivo de una actualización hecha en el servidor (EV-26).</summary>
public enum MotivoActualizacion { ACTUALIZACION, ASOCIACION }

/// <summary>Estado del cortacircuitos de un adaptador (EV-23).</summary>
public enum EstadoIntegracion { CERRADO, ABIERTO, SEMIABIERTO }

/// <summary>Dato que reporta el sistema de proyectos externo (EV-24).</summary>
public enum TipoReporteExterno { AVANCE, CONTRATISTA }

/// <summary>Capas geográficas que entrega el adaptador de SE-2 (EV-21).</summary>
public enum CapaGeografica { MUNICIPIOS, BARRIOS, ZONAS_RIESGO }

public enum Confianza { ALTA, MEDIA, BAJA }
