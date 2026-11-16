using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;

namespace AdaptadorSe7;

/// <summary>Configuración de SE-7, sección "Se7".</summary>
public sealed class OpcionesSe7
{
    public const string Seccion = "Se7";

    /// <summary>Dirección base de la API de SE-7 (en compose y en Kubernetes, el simulador).</summary>
    public string Url { get; set; } = "http://sim-se7:8080/";

    /// <summary>Cambios por página al consultar desde la marca de avance.</summary>
    public int TamanoPagina { get; set; } = 100;
}

/// <summary>
/// Cliente HTTP de SE-7 con la resiliencia de comun (DT-09): implementa el contrato del adaptador (DA-11). Todas sus
/// operaciones son idempotentes (GET y PUT de una versión), así que el reintento se aplica a todas (Cuadro 5).
/// </summary>
public sealed class ClienteSe7 : IContratoAdaptador<CambioObraSe7>, ISondaSistemaExterno
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OpcionesSe7 _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ClienteSe7> _log;

    public ClienteSe7(HttpClient http, OpcionesSe7 opciones, TimeProvider reloj, ILogger<ClienteSe7> log)
    {
        _http = http;
        _opciones = opciones;
        _reloj = reloj;
        _log = log;
    }

    public string SistemaExterno => TraduccionSe7.Sistema;

    public async Task<LoteExterno<CambioObraSe7>> RecuperarDesde(string? marcaAvance, CancellationToken ct)
    {
        string ruta = $"v1/cambios?limite={_opciones.TamanoPagina}"
                      + (marcaAvance is null ? "" : "&desde=" + Uri.EscapeDataString(marcaAvance));
        using HttpResponseMessage respuesta = await _http.GetAsync(ruta, ct);
        Verificar(respuesta, "la consulta de cambios");
        PaginaCambiosSe7 pagina = await respuesta.Content.ReadFromJsonAsync<PaginaCambiosSe7>(Json, ct)
            ?? throw new SistemaExternoNoDisponibleException("SE-7 devolvió una página de cambios vacía.");
        return new LoteExterno<CambioObraSe7>(pagina.Cambios, pagina.Marca, pagina.HayMas);
    }

    public Sobre? Traducir(CambioObraSe7 externo)
    {
        Sobre? evento = TraduccionSe7.AEvento(externo, _reloj.GetUtcNow());
        if (evento is null)
        {
            _log.LogInformation("Cambio {Secuencia} de SE-7 ({Clase}, obra {Obra}) sin evento para la plataforma; se salta.",
                externo.Secuencia, externo.Clase, externo.CodigoObra);
        }
        return evento;
    }

    public async Task<ResultadoEntrega> Entregar(Sobre eventoCanonico, CancellationToken ct)
    {
        PeticionSe7? peticion = TraduccionSe7.ASe7(eventoCanonico);
        if (peticion is null)
        {
            return ResultadoEntrega.Omitido;
        }
        using HttpResponseMessage respuesta = await _http.PutAsJsonAsync(peticion.Ruta, peticion.Cuerpo, Json, ct);
        Verificar(respuesta, $"la versión {peticion.Version} de la obra {peticion.CodigoObra}");
        return respuesta.StatusCode == HttpStatusCode.Created ? ResultadoEntrega.Entregado : ResultadoEntrega.Repetido;
    }

    public async Task SondearAsync(CancellationToken ct)
    {
        using HttpResponseMessage respuesta = await _http.GetAsync("v1/salud", ct);
        Verificar(respuesta, "el sondeo");
    }

    /// <summary>5xx, 408 y 429: SE-7 no está disponible (transitorio). Otro 4xx: rechazó el contenido (permanente).</summary>
    private static void Verificar(HttpResponseMessage respuesta, string que)
    {
        int codigo = (int)respuesta.StatusCode;
        if (respuesta.IsSuccessStatusCode)
        {
            return;
        }
        if (codigo >= 500 || respuesta.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            throw new SistemaExternoNoDisponibleException($"SE-7 respondió {codigo} a {que}.");
        }
        throw new EntregaRechazadaException($"SE-7 rechazó {que} con {codigo}.");
    }
}
