using ConsultaDanos.Dominio;

namespace ConsultaDanos.Aplicacion;

// Hechos que llegan del bus, ya traducidos por el adaptador de entrada. La aplicación no conoce el sobre ni el contrato.

/// <summary>Origen de una entrada del historial del activo (RF-ED-15).</summary>
public static class OrigenHistorial
{
    public const string Sincronizacion = "SINCRONIZACION";
    public const string Actualizacion = "ACTUALIZACION";
    public const string Asociacion = "ASOCIACION";
    public const string Resolucion = "RESOLUCION";
    public const string Validacion = "VALIDACION";
}

/// <summary>
/// Una evaluación con su contenido: EV-09 (sincronizada), EV-26 (actualizada en el servidor). <paramref name="Motivo"/> es
/// el origen de la entrada del historial.
/// </summary>
public sealed record EvaluacionRegistrada(Guid IdActivo, ContenidoActivo Contenido, int VersionActivo, OrigenContenido Origen,
    string Motivo, bool EnConflicto, string? Autor, DateTimeOffset Instante);

/// <summary>EV-08: cambio de validación de una evaluación (RF-ED-08). Trae una copia del contenido de la evaluación.</summary>
public sealed record ValidacionCambiada(Guid IdActivo, ContenidoActivo Contenido, EstadoValidacion Estado, int Version,
    string ValidadoPor, DateTimeOffset InstanteValidacion, DateTimeOffset Instante);

/// <summary>EV-10: conflicto o posible duplicado detectado, que queda pendiente en la bandeja de Evaluación de Daños.</summary>
public sealed record ConflictoDetectado(Guid IdConflicto, Guid IdActivo, string Tipo, Guid? IdEvaluacionEntrante, DateTimeOffset Instante);

/// <summary>EV-22: decisión humana sobre un conflicto (AD-32, AD-33), con el contenido de la evaluación que queda vigente.</summary>
public sealed record ConflictoResuelto(Guid IdConflicto, string Decision, string DecididoPor, string Justificacion,
    Guid IdActivoVigente, ContenidoActivo Vigente, int VersionActivo, DateTimeOffset Instante)
{
    public const string Fusionar = "FUSIONAR";
}

public sealed record DivisionLeida(string Codigo, string Nombre, string? CodigoPadre, string GeometriaGeoJson);

/// <summary>EV-21: una parte de la capa de municipios (una por departamento).</summary>
public sealed record CapaRecibida(IReadOnlyList<DivisionLeida> Divisiones, int Version, DateTimeOffset Instante);

/// <summary>EV-25: nombre de un tipo de activo del catálogo (QA-10: un tipo nuevo se muestra sin redespliegue).</summary>
public sealed record TipoActivoActualizado(string Codigo, string Nombre, int Version, DateTimeOffset Instante);

/// <summary>EV-23: estado del cortacircuitos de un adaptador (R-10).</summary>
public sealed record EstadoIntegracion(string Sistema, string Anterior, string Nuevo, DateTimeOffset Instante, long MensajesEnCola);

/// <summary>Un conflicto como queda en <c>p_conflictos</c> después de aplicar EV-10 o EV-22, lleguen en el orden que lleguen.</summary>
public sealed record ConflictoLeido(Guid IdConflicto, Guid? IdActivo, string Estado, string? Decision, Guid? IdActivoVigente)
{
    /// <summary>Se fusionó un duplicado: el activo del conflicto queda absorbido por el de la evaluación vigente (E-14).</summary>
    public bool FusionaDuplicado => Decision == ConflictoResuelto.Fusionar && IdActivo is not null && IdActivoVigente is not null
        && IdActivo != IdActivoVigente;
}

/// <summary>Una entrada de <c>p_historial_activo</c> (RF-ED-15): una evaluación registrada en el tiempo, con su nivel.</summary>
public sealed record EntradaHistorial(Guid IdActivo, Guid IdEvaluacion, int VersionEvaluacion, string Origen, DateTimeOffset Instante,
    NivelDano Nivel, string TipoActivo, string EstadoEvaluacion, EstadoValidacion? Validacion, bool EnConflicto, Guid? IdDesastre,
    string CodigoMunicipio, string? Autor, string? Detalle);
