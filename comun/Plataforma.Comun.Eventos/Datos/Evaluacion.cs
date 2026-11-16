namespace Plataforma.Comun.Eventos.Datos;

// Eventos de Evaluación de Daños. Campos del SAD (Cuadro 16) y extensiones aditivas (E-02, E-03, E-05, E-06, E-16).

/// <summary>
/// EV-08, evaluación validada (RF-ED-08). Se emite en cada cambio de validación, también al rechazar o devolver a pendiente.
/// Extensiones de E-02: IdActivo, ReferenciaActivo, CodigoMunicipio, CodigoDepartamento, EstadoValidacion, ValidadoPor
/// e InstanteValidacion.
/// </summary>
public sealed record EvaluacionValidadaV1(
    Guid IdEvaluacion,
    Guid IdDesastre,
    string TipoActivo,
    NivelDano NivelDano,
    Ubicacion Ubicacion,
    string Evaluador,
    string Organizacion,
    int Version,
    Guid IdActivo,
    string ReferenciaActivo,
    string CodigoMunicipio,
    string CodigoDepartamento,
    EstadoValidacion EstadoValidacion,
    string ValidadoPor,
    DateTimeOffset InstanteValidacion);

/// <summary>
/// EV-09, evaluación sincronizada desde un dispositivo. Los seis primeros campos son los del SAD.
/// Extensiones de E-03: los datos que Consulta de Daños necesita para su proyección.
/// </summary>
public sealed record EvaluacionSincronizadaV1(
    Guid IdEvaluacion,
    string IdDispositivo,
    DateTimeOffset InstanteLocal,
    DateTimeOffset InstanteServidor,
    bool EnConflicto,
    int VersionesConservadas,
    Guid IdActivo,
    Guid? IdDesastre,
    string TipoActivo,
    NivelDano NivelDano,
    Ubicacion Ubicacion,
    double PrecisionM,
    string CodigoMunicipio,
    EstadoEvaluacion EstadoEvaluacion,
    int VersionEvaluacion,
    int VersionActivo);

/// <summary>EV-10, conflicto o duplicado detectado. Extensiones: IdActivo y TipoConflicto, para marcar el activo en el mapa.</summary>
public sealed record ConflictoDetectadoV1(
    Guid IdConflicto,
    IReadOnlyList<Guid> IdsEvaluaciones,
    string ReglaDeteccion,
    IReadOnlyList<VersionConservada> Versiones,
    EstadoRevision EstadoRevision,
    Guid IdActivo,
    TipoConflicto TipoConflicto);

/// <summary>Una versión que se conserva en un conflicto. Ninguna versión se descarta (AD-32).</summary>
public sealed record VersionConservada(Guid IdEvaluacion, int NumeroVersion, bool Vigente);

/// <summary>Estado de una evaluación después de un cambio hecho en el servidor (usado por EV-22 y EV-26).</summary>
public sealed record InstantaneaEvaluacion(
    Guid IdEvaluacion,
    Guid IdActivo,
    Guid? IdDesastre,
    string TipoActivo,
    NivelDano NivelDano,
    Ubicacion Ubicacion,
    double PrecisionM,
    string CodigoMunicipio,
    EstadoEvaluacion EstadoEvaluacion,
    int VersionEvaluacion,
    int VersionActivo);

/// <summary>EV-22 (E-05), conflicto o duplicado resuelto por un usuario autorizado.</summary>
public sealed record ConflictoResueltoV1(
    Guid IdConflicto,
    DecisionConflicto Decision,
    Guid IdEvaluacionVigente,
    int VersionVigente,
    string DecididoPor,
    string Justificacion,
    InstantaneaEvaluacion Evaluacion);

/// <summary>EV-26 (E-06), evaluación actualizada en el servidor: actualización con conexión o asociación a un desastre.</summary>
public sealed record EvaluacionActualizadaV1(
    Guid IdEvaluacion,
    MotivoActualizacion Motivo,
    string Autor,
    InstantaneaEvaluacion Evaluacion);

/// <summary>EV-25 (E-16), tipo de activo creado o modificado en el catálogo (AD-28).</summary>
public sealed record TipoActivoActualizadoV1(
    string Codigo,
    string Nombre,
    IReadOnlyList<CriterioNivel> Criterios,
    int Version);

public sealed record CriterioNivel(NivelDano Nivel, string Criterio);
