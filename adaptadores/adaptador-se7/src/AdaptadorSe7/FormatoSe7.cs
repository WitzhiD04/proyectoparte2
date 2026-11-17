namespace AdaptadorSe7;

// Formato de SE-7 tal como lo ve el adaptador (AD-03): obras, versiones y cambios. Solo existe en este proyecto; el
// resto de la plataforma conoce únicamente el contrato de comun (AD-09).

/// <summary>Situación de una obra en SE-7.</summary>
public static class SituacionSe7
{
    public const string Formulada = "FORMULADA";
    public const string Aprobada = "APROBADA";
    public const string EnObra = "EN_OBRA";
    public const string Terminada = "TERMINADA";
    public const string Anulada = "ANULADA";
}

/// <summary>Cuerpo de <c>PUT /v1/obras/{codigoObra}/versiones/{version}</c>.</summary>
public sealed record VersionObraSe7(
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

/// <summary>Una entrega a SE-7: la obra, la versión (clave de idempotencia del Cuadro 14) y el cuerpo.</summary>
public sealed record PeticionSe7(string CodigoObra, int Version, VersionObraSe7 Cuerpo)
{
    public string Ruta => $"v1/obras/{Uri.EscapeDataString(CodigoObra)}/versiones/{Version}";
}

/// <summary>Un cambio que reporta SE-7 en <c>GET /v1/cambios</c>.</summary>
public sealed record CambioObraSe7(long Secuencia, string CodigoObra, string Clase, decimal? PorcentajeAvance, string? Contratista,
    DateTimeOffset OcurridoEn);

public sealed record PaginaCambiosSe7(IReadOnlyList<CambioObraSe7> Cambios, string? Marca, bool HayMas);
