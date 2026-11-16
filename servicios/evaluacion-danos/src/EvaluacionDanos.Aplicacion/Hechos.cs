using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion;

// Hechos del servicio que interesan a otros módulos. La aplicación los entrega por el puerto IPublicadorHechos sin
// conocer el bus: el adaptador de infraestructura los traduce al contrato de eventos (EV-08, EV-09, EV-10, EV-22,
// EV-25 y EV-26) y los deja en la bandeja de salida, en la misma transacción que el cambio (AD-45).

public abstract record Hecho(DateTime Instante);

/// <summary>Estado de una evaluación en el momento del hecho, con lo que una proyección necesita (E-03).</summary>
public sealed record InstantaneaEvaluacion(
    Guid IdEvaluacion,
    Guid IdActivo,
    Guid? IdDesastre,
    string TipoActivoCodigo,
    NivelDano NivelDano,
    double Lat,
    double Lon,
    double PrecisionM,
    string CodigoMunicipio,
    EstadoEvaluacion Estado,
    int VersionEvaluacion,
    int VersionActivo)
{
    public static InstantaneaEvaluacion De(Evaluacion evaluacion, Activo activo, int versionEvaluacion) => new(
        evaluacion.Id, activo.Id, evaluacion.DesastreId, evaluacion.TipoActivoCodigo, evaluacion.NivelDano,
        evaluacion.Lat, evaluacion.Lon, evaluacion.PrecisionM, activo.CodigoMunicipio, evaluacion.Estado,
        versionEvaluacion, activo.Version);
}

/// <summary>Una operación de un dispositivo quedó guardada (aplicada o en conflicto). Se traduce a EV-09.</summary>
/// <param name="VersionesConservadas">Versiones guardadas de la evaluación, más la vigente si quedó en conflicto.</param>
public sealed record EvaluacionSincronizada(
    InstantaneaEvaluacion Evaluacion,
    string IdDispositivo,
    DateTime InstanteLocal,
    bool EnConflicto,
    int VersionesConservadas,
    DateTime Instante) : Hecho(Instante);

public sealed record VersionEnConflicto(Guid IdEvaluacion, int NumeroVersion, bool Vigente);

/// <summary>Se detectó un conflicto de versión o un posible duplicado; ninguna versión se descarta. EV-10.</summary>
public sealed record ConflictoDetectado(
    Guid IdConflicto,
    Guid IdActivo,
    TipoConflicto Tipo,
    IReadOnlyList<Guid> IdsEvaluaciones,
    string Regla,
    IReadOnlyList<VersionEnConflicto> Versiones,
    DateTime Instante) : Hecho(Instante);

/// <summary>Cambió la validación de una evaluación (RF-ED-08). EV-08, también al rechazar o devolver a pendiente.</summary>
/// <param name="IdDesastre">El desastre bajo el cual se validó: Reconstrucción lo usa para sus candidatas.</param>
public sealed record ValidacionCambiada(
    InstantaneaEvaluacion Evaluacion,
    Guid IdDesastre,
    string Evaluador,
    string Organizacion,
    string ReferenciaActivo,
    EstadoValidacion Estado,
    string ValidadoPor,
    DateTime Instante) : Hecho(Instante);

/// <summary>Actualización con conexión (RF-ED-07) o asociación a un desastre (AD-29). EV-26.</summary>
public sealed record EvaluacionActualizada(
    InstantaneaEvaluacion Evaluacion,
    MotivoActualizacion Motivo,
    string Autor,
    DateTime Instante) : Hecho(Instante);

/// <summary>Un usuario autorizado resolvió un conflicto o un duplicado (AD-32, AD-33). EV-22.</summary>
public sealed record ConflictoResuelto(
    Guid IdConflicto,
    DecisionConflicto Decision,
    string DecididoPor,
    string Justificacion,
    InstantaneaEvaluacion Vigente,
    DateTime Instante) : Hecho(Instante);

/// <summary>Se creó o cambió un tipo de activo del catálogo (AD-28, QA-10). EV-25.</summary>
public sealed record TipoActivoActualizado(
    string Codigo,
    string Nombre,
    IReadOnlyList<CriterioDto> Criterios,
    int Version,
    DateTime Instante) : Hecho(Instante);
