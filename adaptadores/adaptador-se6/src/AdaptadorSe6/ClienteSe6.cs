using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;

namespace AdaptadorSe6;

/// <summary>Configuración de SE-6 y de los lotes de salida, sección "Se6" (DA-11, Cuadro 14).</summary>
public sealed class OpcionesSe6
{
    public const string Seccion = "Se6";

    /// <summary>Dirección base de la API de SE-6 (en compose y en Kubernetes, el simulador).</summary>
    public string Url { get; set; } = "http://sim-se6:8080/";

    /// <summary>Cantidad máxima de mensajes acumulados antes de cerrar y entregar un lote a SE-6.</summary>
    public int TamanoLote { get; set; } = 10;

    /// <summary>Tiempo máximo (en segundos) que espera un lote abierto desde su primer mensaje antes de entregarse.</summary>
    public double VentanaLoteSegundos { get; set; } = 2;

    public static OpcionesSe6 Leer(IConfiguration configuracion)
    {
        OpcionesSe6 opciones = new();
        configuracion.GetSection(Seccion).Bind(opciones);
        return opciones;
    }
}

/// <summary>Contrato de salida por lotes hacia SE-6 (DA-11).</summary>
public interface ISalidaLotesSe6 : ISalidaAdaptador, ISondaSistemaExterno
{
    Task<ResultadoEntrega> EntregarLoteAsync(IReadOnlyList<Sobre> eventosCanonicos, CancellationToken ct);
}

/// <summary>
/// Cliente HTTP de SE-6 con la resiliencia común (DT-09, AD-05, AD-37): entrega lotes con <c>PUT /v1/lotes/{idLote}</c>
/// (método idempotente según Cuadro 5, reintentado por Polly y protegido por el cortacircuitos que publica EV-23).
/// </summary>
public sealed class ClienteSe6 : ISalidaLotesSe6
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ClienteSe6(HttpClient http)
    {
        _http = http;
    }

    public string SistemaExterno => TraduccionSe6.Sistema;

    public Task<ResultadoEntrega> Entregar(Sobre eventoCanonico, CancellationToken ct) =>
        EntregarLoteAsync([eventoCanonico], ct);

    public async Task<ResultadoEntrega> EntregarLoteAsync(IReadOnlyList<Sobre> eventosCanonicos, CancellationToken ct)
    {
        PeticionLoteSe6? peticion = TraduccionSe6.ALote(eventosCanonicos);
        if (peticion is null)
        {
            return ResultadoEntrega.Omitido;
        }
        using HttpResponseMessage respuesta = await _http.PutAsJsonAsync(peticion.Ruta, peticion.Cuerpo, Json, ct);
        Verificar(respuesta, $"el lote {peticion.IdLote} ({peticion.Cuerpo.Movimientos.Count} movimientos)");
        return respuesta.StatusCode == HttpStatusCode.Created ? ResultadoEntrega.Entregado : ResultadoEntrega.Repetido;
    }

    public async Task SondearAsync(CancellationToken ct)
    {
        using HttpResponseMessage respuesta = await _http.GetAsync("v1/salud", ct);
        Verificar(respuesta, "el sondeo");
    }

    /// <summary>5xx, 408 y 429: SE-6 no está disponible (transitorio). Otro 4xx: rechazó el contenido (permanente).</summary>
    private static void Verificar(HttpResponseMessage respuesta, string que)
    {
        int codigo = (int)respuesta.StatusCode;
        if (respuesta.IsSuccessStatusCode)
        {
            return;
        }
        if (codigo >= 500 || respuesta.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            throw new SistemaExternoNoDisponibleException($"SE-6 respondió {codigo} a {que}.");
        }
        throw new EntregaRechazadaException($"SE-6 rechazó {que} con {codigo}.");
    }
}
