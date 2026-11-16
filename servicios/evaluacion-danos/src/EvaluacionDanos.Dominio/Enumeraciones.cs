namespace EvaluacionDanos.Dominio;

// Los nombres van tal cual se escriben en el JSON y en la base ("SIN_DANO"), así no hace falta
// traducir entre el texto del contrato con la app y el código. Se guardan como texto.
// Son las enumeraciones del servicio: la traducción a las canónicas del bus vive en Infraestructura (AD-09).

public enum NivelDano
{
    SIN_DANO,
    LEVE,
    MODERADO,
    GRAVE,
    DESTRUCCION_TOTAL
}

/// <summary>DUPLICADO: descartada al fusionar dos posibles duplicados (E-14). Conserva versiones y evidencias.</summary>
public enum EstadoEvaluacion
{
    PRELIMINAR,
    REGISTRADA,
    EN_CONFLICTO,
    VALIDADA,
    RECHAZADA,
    DUPLICADO
}

public enum TipoConflicto
{
    VERSION,
    DUPLICADO
}

public enum EstadoConflicto
{
    PENDIENTE,
    RESUELTO
}

public enum TipoOperacion
{
    CREAR,
    ACTUALIZAR
}

public enum ResultadoOperacion
{
    APLICADA,
    EN_CONFLICTO,
    POSIBLE_DUPLICADO,
    INVALIDA
}

/// <summary>Resultado de la validación de una evaluación (RF-ED-08).</summary>
public enum EstadoValidacion
{
    VALIDADA,
    RECHAZADA,
    PENDIENTE
}

/// <summary>Decisión humana sobre un conflicto o un posible duplicado (AD-32, AD-33).</summary>
public enum DecisionConflicto
{
    PREVALECE_ENTRANTE,
    PREVALECE_VIGENTE,
    FUSIONAR,
    RECHAZAR_DUPLICADO
}

/// <summary>Por qué existe una versión del historial de una evaluación (AD-30).</summary>
public enum MotivoVersion
{
    SINCRONIZACION,
    ACTUALIZACION,
    ASOCIACION,
    VALIDACION,
    RESOLUCION
}

/// <summary>Motivo de un cambio hecho en el servidor sin pasar por la sincronización (E-06).</summary>
public enum MotivoActualizacion
{
    ACTUALIZACION,
    ASOCIACION
}

/// <summary>Estados del ciclo de vida de un desastre (enunciado §4.1), para la copia local (E-29).</summary>
public enum EstadoDesastre
{
    DETECTADO,
    REGISTRADO,
    EN_EVALUACION,
    EN_ATENCION,
    EN_RECUPERACION,
    EN_RECONSTRUCCION,
    CERRADO
}
