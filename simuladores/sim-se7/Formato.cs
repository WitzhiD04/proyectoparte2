namespace SimSe7;

// Formato propio de SE-7. Es distinto del contrato de la plataforma a propósito: el adaptador traduce (AD-03, AD-09).
// Vocabulario de SE-7: una "obra" (no un proyecto), su "situacion" y sus "versiones".

/// <summary>Situación de una obra en SE-7.</summary>
public static class Situaciones
{
    public const string Formulada = "FORMULADA";
    public const string Aprobada = "APROBADA";
    public const string EnObra = "EN_OBRA";
    public const string Terminada = "TERMINADA";
    public const string Anulada = "ANULADA";

    public static readonly IReadOnlySet<string> Todas = new HashSet<string> { Formulada, Aprobada, EnObra, Terminada, Anulada };
}

/// <summary>
/// Una versión de una obra que la plataforma registra en SE-7: <c>PUT /v1/obras/{codigoObra}/versiones/{version}</c>.
/// La URL es la clave de idempotencia (Cuadro 14: clave del proyecto y número de versión): repetir la misma versión no
/// tiene efecto.
/// </summary>
/// <param name="Operacion">ALTA (la obra se crea) o MODIFICACION.</param>
/// <param name="Concepto">Qué cambió: ALTA, RESPONSABLE, CRONOGRAMA, CONTRATISTA, ESTADO, AVANCE, COSTO o DOCUMENTO.</param>
public sealed record VersionObra(
    string Operacion,
    string Concepto,
    string Titulo,
    string CodigoDesastre,
    string Situacion,
    decimal PorcentajeAvance,
    decimal? Presupuesto,
    decimal CostoEjecutado,
    IReadOnlyList<string>? Municipios = null,
    string? Responsable = null,
    string? FuenteRecursos = null,
    DateOnly? FechaInicio = null,
    DateOnly? FechaFin = null,
    string? Contratista = null,
    string? ValorAnterior = null,
    string? ValorNuevo = null,
    string? Nota = null);

/// <summary>Respuesta al registrar una versión. <c>Registrada</c> es falso si SE-7 ya la tenía.</summary>
public sealed record RespuestaVersion(string CodigoObra, int Version, bool Registrada);

/// <summary>Un cambio que SE-7 reporta: avance de obra o contratista asignado.</summary>
/// <param name="Secuencia">Posición en el registro de cambios de SE-7; es la marca de avance.</param>
/// <param name="Clase">AVANCE o CONTRATISTA.</param>
public sealed record CambioObra(long Secuencia, string CodigoObra, string Clase, decimal? PorcentajeAvance, string? Contratista,
    DateTimeOffset OcurridoEn);

/// <summary>Página de <c>GET /v1/cambios?desde={marca}&amp;limite={n}</c>. <c>Marca</c> es la del último cambio de la página.</summary>
public sealed record PaginaCambios(IReadOnlyList<CambioObra> Cambios, string? Marca, bool HayMas);

/// <summary>Cuerpo de <c>POST /admin/caida</c>: on (responde 503), off (normal) o lento (responde después del retraso).</summary>
public sealed record SolicitudCaida(string Modo);

/// <summary>Cuerpo de <c>POST /admin/cambios</c>: un cambio hecho a mano en SE-7 (demostraciones y pruebas).</summary>
public sealed record SolicitudCambio(string CodigoObra, string Clase, decimal? PorcentajeAvance, string? Contratista);

public sealed record EstadoObra(string CodigoObra, string Titulo, string Situacion, decimal PorcentajeAvance, string? Contratista,
    IReadOnlyList<int> Versiones, int EntregasRepetidas);

public sealed record EstadoSimulador(string Modo, IReadOnlyList<EstadoObra> Obras, long Cambios, int VersionesRegistradas,
    int EntregasRepetidas);
