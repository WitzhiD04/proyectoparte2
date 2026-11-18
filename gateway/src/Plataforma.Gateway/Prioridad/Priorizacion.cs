using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Plataforma.Gateway.Catalogo;

namespace Plataforma.Gateway.Prioridad;

/// <summary>Parámetros de la priorización (AD-19). Son de despliegue: cambiarlos no toca el código (QA-05 c).</summary>
public sealed class OpcionesPrioridad
{
    public const string Seccion = "Gateway:Prioridad";

    /// <summary>Peticiones simultáneas reservadas para la consola de coordinación.</summary>
    public int ConcurrenciaConsola { get; set; } = 64;
    public int ColaConsola { get; set; } = 128;
    /// <summary>Peticiones simultáneas que comparten el portal y el móvil (el remanente).</summary>
    public int ConcurrenciaCompartida { get; set; } = 128;
    public int ColaCompartida { get; set; } = 64;
    /// <summary>Rutas cuya respuesta se guarda y se sirve degradada cuando la clase se satura (GET de /v1/consulta*).</summary>
    public string PrefijoDegradable { get; set; } = "/v1/consulta";
    public int MaxEntradasGuardadas { get; set; } = 512;
    public int MaxBytesGuardados { get; set; } = 1_048_576;
}

/// <summary>
/// Limitador con particiones por clase de tráfico (DA-16): la consola tiene su propia concurrencia, reservada, y el
/// portal y el móvil comparten el resto. Así una avalancha del portal no le quita capacidad a la consola (QA-05 e).
/// </summary>
public sealed class LimitadorPorClase : IDisposable
{
    private readonly ConcurrencyLimiter _consola;
    private readonly ConcurrencyLimiter _compartido;

    public LimitadorPorClase(OpcionesPrioridad opciones)
    {
        _consola = Crear(opciones.ConcurrenciaConsola, opciones.ColaConsola);
        _compartido = Crear(opciones.ConcurrenciaCompartida, opciones.ColaCompartida);
    }

    public ValueTask<RateLimitLease> AdquirirAsync(string claseTrafico, CancellationToken ct) =>
        (claseTrafico == ClasesTrafico.Consola ? _consola : _compartido).AcquireAsync(1, ct);

    public void Dispose()
    {
        _consola.Dispose();
        _compartido.Dispose();
    }

    private static ConcurrencyLimiter Crear(int permisos, int cola) => new(new ConcurrencyLimiterOptions
    {
        PermitLimit = Math.Max(1, permisos),
        QueueLimit = Math.Max(0, cola),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });
}

/// <summary>Última respuesta correcta de una consulta, para servirla degradada si la clase se satura.</summary>
public sealed record RespuestaGuardada(string? TipoContenido, string? Codificacion, byte[] Cuerpo, DateTimeOffset Instante);

/// <summary>
/// Guarda la última respuesta 200 de cada consulta (ruta y parámetros). Las consultas de lectura (Consulta y Consulta
/// de Daños) responden lo mismo a cualquier usuario autenticado, por eso la clave no incluye la identidad.
/// </summary>
public sealed class RespuestasGuardadas
{
    private readonly ConcurrentDictionary<string, RespuestaGuardada> _respuestas = new(StringComparer.Ordinal);
    private readonly OpcionesPrioridad _opciones;

    public RespuestasGuardadas(OpcionesPrioridad opciones)
    {
        _opciones = opciones;
    }

    public void Guardar(string clave, RespuestaGuardada respuesta)
    {
        _respuestas[clave] = respuesta;
        if (_respuestas.Count > _opciones.MaxEntradasGuardadas)
        {
            // Se descarta la más vieja: es la que menos sirve como respuesta degradada.
            KeyValuePair<string, RespuestaGuardada> vieja = _respuestas.MinBy(p => p.Value.Instante);
            _respuestas.TryRemove(vieja.Key, out _);
        }
    }

    public bool Buscar(string clave, out RespuestaGuardada? respuesta) => _respuestas.TryGetValue(clave, out respuesta);

    public static string Clave(HttpRequest peticion) => peticion.Path.Value + peticion.QueryString.Value;
}

/// <summary>
/// Priorización y degradación (AD-19, DA-16) en el pipeline de YARP. Cada petición toma un permiso de la partición de
/// su clase de tráfico (metadato <c>claseTrafico</c> de la ruta). Si no lo consigue y es una consulta (GET de
/// /v1/consulta*), responde la última respuesta guardada marcada como degradada (<c>X-Degradado: true</c> y
/// <c>degradado: true</c> en un objeto JSON), no un 503 (CON-6). Solo si nunca hubo una respuesta guardada, o si no es
/// una consulta, responde 503 con <c>Retry-After</c>.
/// </summary>
public sealed class Priorizacion
{
    public const string EncabezadoDegradado = "X-Degradado";
    public const string EncabezadoDatoAl = "X-Degradado-Dato-Al";

    private readonly RequestDelegate _siguiente;
    private readonly LimitadorPorClase _limitador;
    private readonly RespuestasGuardadas _guardadas;
    private readonly OpcionesPrioridad _opciones;
    private readonly ILogger<Priorizacion> _log;

    public Priorizacion(RequestDelegate siguiente, LimitadorPorClase limitador, RespuestasGuardadas guardadas,
        OpcionesPrioridad opciones, ILogger<Priorizacion> log)
    {
        _siguiente = siguiente;
        _limitador = limitador;
        _guardadas = guardadas;
        _opciones = opciones;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        string clase = contexto.GetReverseProxyFeature().Route.Config.Metadata?.GetValueOrDefault(CatalogoRutas.ClaseTrafico)
            ?? ClasesTrafico.Portal;
        bool degradable = HttpMethods.IsGet(contexto.Request.Method)
            && contexto.Request.Path.StartsWithSegmentsOrPrefix(_opciones.PrefijoDegradable);

        using RateLimitLease permiso = await _limitador.AdquirirAsync(clase, contexto.RequestAborted);
        if (!permiso.IsAcquired)
        {
            if (degradable && _guardadas.Buscar(RespuestasGuardadas.Clave(contexto.Request), out RespuestaGuardada? guardada))
            {
                await ResponderDegradada(contexto, guardada!);
                return;
            }
            _log.LogWarning("Clase de tráfico {Clase} saturada: {Metodo} {Ruta}", clase, contexto.Request.Method, contexto.Request.Path);
            contexto.Response.Headers.RetryAfter = "1";
            await Seguridad.AutorizacionNivel1.Responder(contexto, StatusCodes.Status503ServiceUnavailable, "SATURADO",
                "La plataforma está atendiendo demasiadas peticiones de este tipo; intente de nuevo en un momento.");
            return;
        }

        if (!degradable)
        {
            await _siguiente(contexto);
            return;
        }
        await AtenderYGuardar(contexto);
    }

    private async Task AtenderYGuardar(HttpContext contexto)
    {
        Stream original = contexto.Response.Body;
        await using CopiaLimitada copia = new(original, _opciones.MaxBytesGuardados);
        contexto.Response.Body = copia;
        try
        {
            await _siguiente(contexto);
        }
        finally
        {
            contexto.Response.Body = original;
        }
        if (contexto.Response.StatusCode == StatusCodes.Status200OK && copia.Completa)
        {
            _guardadas.Guardar(RespuestasGuardadas.Clave(contexto.Request), new RespuestaGuardada(
                contexto.Response.ContentType, contexto.Response.Headers.ContentEncoding.FirstOrDefault(), copia.Copia(),
                DateTimeOffset.UtcNow));
        }
    }

    internal static async Task ResponderDegradada(HttpContext contexto, RespuestaGuardada guardada)
    {
        byte[] cuerpo = guardada.Cuerpo;
        if (guardada.Codificacion is null && guardada.TipoContenido?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            // En un objeto JSON se marca también en el cuerpo; un arreglo o un cuerpo comprimido van tal cual, con el
            // encabezado.
            try
            {
                if (JsonNode.Parse(cuerpo) is JsonObject objeto)
                {
                    objeto["degradado"] = true;
                    cuerpo = System.Text.Encoding.UTF8.GetBytes(objeto.ToJsonString());
                }
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }
        contexto.Response.StatusCode = StatusCodes.Status200OK;
        contexto.Response.ContentType = guardada.TipoContenido;
        if (guardada.Codificacion is not null)
        {
            contexto.Response.Headers.ContentEncoding = guardada.Codificacion;
        }
        contexto.Response.Headers[EncabezadoDegradado] = "true";
        contexto.Response.Headers[EncabezadoDatoAl] = guardada.Instante.ToString("O");
        contexto.Response.ContentLength = cuerpo.Length;
        await contexto.Response.Body.WriteAsync(cuerpo, contexto.RequestAborted);
    }
}

internal static class RutasExtensiones
{
    /// <summary>"/v1/consulta" cubre /v1/consulta/... y también /v1/consulta-danos/... (todas las consultas).</summary>
    public static bool StartsWithSegmentsOrPrefix(this PathString ruta, string prefijo) =>
        ruta.Value?.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Escribe al cliente y, a la vez, guarda una copia hasta un máximo de bytes.</summary>
internal sealed class CopiaLimitada : Stream
{
    private readonly Stream _destino;
    private readonly int _maximo;
    private readonly MemoryStream _copia = new();

    public CopiaLimitada(Stream destino, int maximo)
    {
        _destino = destino;
        _maximo = maximo;
    }

    /// <summary>Falso si la respuesta superó el máximo: no se guarda una respuesta cortada.</summary>
    public bool Completa { get; private set; } = true;

    public byte[] Copia() => _copia.ToArray();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> datos, CancellationToken ct = default)
    {
        await _destino.WriteAsync(datos, ct);
        Copiar(datos.Span);
    }

    public override async Task WriteAsync(byte[] datos, int inicio, int cantidad, CancellationToken ct) =>
        await WriteAsync(datos.AsMemory(inicio, cantidad), ct);

    public override void Write(byte[] datos, int inicio, int cantidad)
    {
        _destino.Write(datos, inicio, cantidad);
        Copiar(datos.AsSpan(inicio, cantidad));
    }

    private void Copiar(ReadOnlySpan<byte> datos)
    {
        if (!Completa)
        {
            return;
        }
        if (_copia.Length + datos.Length > _maximo)
        {
            Completa = false;
            _copia.SetLength(0);
            return;
        }
        _copia.Write(datos);
    }

    public override Task FlushAsync(CancellationToken ct) => _destino.FlushAsync(ct);
    public override void Flush() => _destino.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        await _copia.DisposeAsync();
        await base.DisposeAsync();
    }
}
