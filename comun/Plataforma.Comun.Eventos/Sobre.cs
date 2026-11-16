using System.Text.Json;

namespace Plataforma.Comun.Eventos;

/// <summary>
/// Sobre canónico de todos los eventos del bus (SAD §5.2, AD-09).
/// Los ocho campos del SAD más <see cref="SecuenciaProductor"/> (mitigación de ATAM R-02) y los datos del evento.
/// </summary>
/// <param name="IdEvento">Identificador único del evento.</param>
/// <param name="Tipo">Identificador del catálogo, por ejemplo "EV-18".</param>
/// <param name="Nombre">Nombre del evento, por ejemplo "proyecto.creado".</param>
/// <param name="Version">Versión del esquema de los datos.</param>
/// <param name="InstanteEmision">Instante en que el productor registró el hecho, en UTC.</param>
/// <param name="Productor">Microservicio o adaptador que emite el evento.</param>
/// <param name="SecuenciaProductor">Número consecutivo y sin huecos por productor. Lo asigna el relevo al publicar.</param>
/// <param name="IdCorrelacion">Identificador de la petición o el flujo que originó el evento.</param>
/// <param name="ClaveIdempotencia">Clave con la que un consumidor descarta un evento repetido (AD-02, AD-14).</param>
/// <param name="Procedencia">Fuente del dato y su nivel de confianza (AD-11, CON-1).</param>
/// <param name="Datos">Contenido del evento, según el esquema del tipo y la versión.</param>
public sealed record Sobre(
    Guid IdEvento,
    string Tipo,
    string Nombre,
    int Version,
    DateTimeOffset InstanteEmision,
    string Productor,
    long? SecuenciaProductor,
    string? IdCorrelacion,
    string ClaveIdempotencia,
    Procedencia Procedencia,
    JsonElement Datos)
{
    /// <summary>Arma un sobre para un evento del catálogo. La secuencia del productor se asigna al publicar.</summary>
    public static Sobre Crear<TDatos>(
        DefinicionEvento definicion,
        string productor,
        string claveIdempotencia,
        TDatos datos,
        DateTimeOffset instanteEmision,
        Procedencia? procedencia = null,
        string? idCorrelacion = null,
        Guid? idEvento = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productor);
        ArgumentException.ThrowIfNullOrWhiteSpace(claveIdempotencia);
        return new Sobre(
            idEvento ?? Guid.NewGuid(),
            definicion.Tipo,
            definicion.Nombre,
            definicion.Version,
            instanteEmision.ToUniversalTime(),
            productor,
            null,
            idCorrelacion,
            claveIdempotencia,
            procedencia ?? Procedencia.Plataforma,
            JsonSerializer.SerializeToElement(datos, JsonContrato.Opciones));
    }

    /// <summary>Lee los datos con el tipo del evento.</summary>
    public TDatos LeerDatos<TDatos>()
    {
        return Datos.Deserialize<TDatos>(JsonContrato.Opciones)
            ?? throw new JsonException($"El evento {Tipo} no trae datos.");
    }

    public string Serializar() => JsonSerializer.Serialize(this, JsonContrato.Opciones);

    public static Sobre Leer(string json)
    {
        return JsonSerializer.Deserialize<Sobre>(json, JsonContrato.Opciones)
            ?? throw new JsonException("El mensaje no contiene un sobre.");
    }

    public static Sobre Leer(ReadOnlySpan<byte> json)
    {
        return JsonSerializer.Deserialize<Sobre>(json, JsonContrato.Opciones)
            ?? throw new JsonException("El mensaje no contiene un sobre.");
    }
}

/// <summary>Procedencia de un dato (AD-11). Los datos que se originan en la plataforma tienen confianza alta.</summary>
public sealed record Procedencia(string Fuente, string? SistemaOrigen, Confianza Confianza)
{
    public const string FuentePlataforma = "plataforma";
    public const string FuenteSistemaExterno = "sistema-externo";

    public static Procedencia Plataforma { get; } = new(FuentePlataforma, null, Confianza.ALTA);

    public static Procedencia DeSistemaExterno(string sistema, Confianza confianza = Confianza.ALTA) =>
        new(FuenteSistemaExterno, sistema, confianza);
}
