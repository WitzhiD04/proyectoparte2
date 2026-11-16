using Auditoria.Dominio;

namespace Auditoria.Aplicacion;

/// <summary>
/// Un evento recibido, ya traducido por el adaptador de entrada. El sobre viaja como texto opaco: la aplicación no
/// conoce el formato del bus, solo lo guarda.
/// </summary>
public sealed record ComandoRegistrarEvento(
    Guid IdEvento,
    string Tipo,
    string Nombre,
    int Version,
    string Productor,
    long? SecuenciaProductor,
    DateTimeOffset InstanteEmision,
    string? IdCorrelacion,
    string ClaveIdempotencia,
    Interpretacion Interpretacion,
    ProcedenciaDato Procedencia,
    string Sobre);

/// <summary>Filtro de la consulta del rastro (DA-17). Todos los campos son opcionales y se combinan con Y.</summary>
/// <param name="Desde">Instante de emisión mínimo, inclusive.</param>
/// <param name="Hasta">Instante de emisión máximo, inclusive.</param>
public sealed record FiltroRegistro(
    string? Entidad = null,
    string? IdEntidad = null,
    string? Actor = null,
    string? Tipo = null,
    string? IdCorrelacion = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null);

public sealed record PaginaEntradas(IReadOnlyList<EntradaAuditoria> Entradas, long Total);

public sealed record ProcedenciaDto(string Fuente, string? SistemaOrigen, string Confianza);

public sealed record EntradaDto(
    Guid IdEvento,
    string Tipo,
    string Nombre,
    int Version,
    string Productor,
    long? SecuenciaProductor,
    DateTimeOffset InstanteEmision,
    DateTimeOffset InstanteRegistro,
    string? IdCorrelacion,
    string ClaveIdempotencia,
    string? Actor,
    string? Entidad,
    string? IdEntidad,
    string? Accion,
    string? Resultado,
    string? ValorAnterior,
    string? ValorNuevo,
    ProcedenciaDto Procedencia,
    string Sobre);

public sealed record PaginaRegistroDto(int Pagina, int Tamano, long Total, IReadOnlyList<EntradaDto> Entradas);

/// <summary>Un hueco reportado. Estado ABIERTO: el evento sigue sin llegar; CUBIERTO: llegó después del plazo.</summary>
public sealed record HuecoDto(string Productor, long Secuencia, DateTimeOffset DetectadoEn, DateTimeOffset? ReportadoEn,
    DateTimeOffset? CubiertoEn, string Estado);

/// <param name="Faltantes">Números reportados que siguen sin llegar.</param>
/// <param name="EnPlazo">Números que faltan pero todavía están dentro del plazo (aún no son un hueco reportado).</param>
public sealed record ProductorDto(string Productor, long MayorContiguo, long MayorRecibido, IReadOnlyList<long> Faltantes,
    IReadOnlyList<long> EnPlazo, DateTimeOffset? RevisadoEn);

public sealed record HuecosDto(int PlazoSegundos, IReadOnlyList<ProductorDto> Productores, IReadOnlyList<HuecoDto> Reportados);

/// <summary>Parámetros de la detección de huecos.</summary>
/// <param name="Plazo">Tiempo que un hueco debe persistir para reportarse. Por omisión, 60 s.</param>
/// <param name="Lote">Números que se leen por productor en cada revisión.</param>
public sealed record OpcionesHuecos(TimeSpan Plazo, int Lote = 5_000)
{
    public static OpcionesHuecos PorOmision { get; } = new(TimeSpan.FromSeconds(60));
}

public sealed record ResumenRevision(int Productores, int Detectados, int Cubiertos, int Reportados);
