using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Plataforma.Gateway.Prioridad;

namespace Plataforma.Gateway.Pruebas;

/// <summary>
/// Priorización por clase de tráfico y degradación (AD-19, DA-16, QA-05 e): con el portal saturado, la consola sigue
/// dentro de su objetivo de latencia y las consultas del portal reciben la última respuesta, marcada como degradada,
/// en vez de un 503 (CON-6).
/// </summary>
public class PriorizacionPruebas : IAsyncLifetime
{
    /// <summary>Objetivo de latencia de la consulta frecuente (QA-06 y QA-05 a: p95 menor que 2 s).</summary>
    private static readonly TimeSpan ObjetivoConsola = TimeSpan.FromSeconds(2);

    private readonly EmisorTokens _tokens = new();
    private ServicioSimulado _servicio = null!;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _gateway = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        _servicio = await ServicioSimulado.IniciarAsync();
        // Capacidad pequeña para saturar con pocas peticiones: 4 reservadas para la consola y 2 compartidas sin cola.
        _gateway = GatewayDePrueba.Crear(CatalogoDePrueba.Escribir(_servicio.Direccion), _tokens, new Dictionary<string, string>
        {
            ["Gateway:Prioridad:ConcurrenciaConsola"] = "4",
            ["Gateway:Prioridad:ColaConsola"] = "64",
            ["Gateway:Prioridad:ConcurrenciaCompartida"] = "2",
            ["Gateway:Prioridad:ColaCompartida"] = "0",
        });
        _cliente = _gateway.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _gateway.DisposeAsync();
        await _servicio.DisposeAsync();
        _tokens.Dispose();
    }

    private Task<HttpResponseMessage> Pedir(string ruta, string token) =>
        _cliente.SendAsync(GatewayDePrueba.Peticion(HttpMethod.Get, ruta, token));

    [Fact]
    public async Task Con_el_portal_saturado_la_consola_sigue_dentro_de_su_objetivo_y_el_portal_se_degrada()
    {
        _servicio.Demoras["/v1/consulta/desastres"] = TimeSpan.FromMilliseconds(400);
        _servicio.Demoras["/v1/proyectos"] = TimeSpan.FromMilliseconds(20);
        string ciudadano = _tokens.Token("ciudadano", "organismo");
        string coordinador = _tokens.Token("alcaldia.armenia", "autoridad");

        // Una consulta normal deja guardada la respuesta.
        Assert.Equal(HttpStatusCode.OK, (await Pedir("/v1/consulta/desastres", ciudadano)).StatusCode);

        // Avalancha del portal: 80 consultas a la vez contra 2 permisos compartidos.
        List<Task<HttpResponseMessage>> portal = Enumerable.Range(0, 80)
            .Select(_ => Pedir("/v1/consulta/desastres", ciudadano)).ToList();
        // Mientras tanto, la consola trabaja: 40 peticiones, de a 4.
        List<TimeSpan> latencias = new();
        List<HttpStatusCode> codigosConsola = new();
        for (int tanda = 0; tanda < 10; tanda++)
        {
            IEnumerable<Task<(HttpStatusCode, TimeSpan)>> pedidas = Enumerable.Range(0, 4).Select(async _ =>
            {
                Stopwatch reloj = Stopwatch.StartNew();
                HttpResponseMessage r = await Pedir("/v1/proyectos", coordinador);
                return (r.StatusCode, reloj.Elapsed);
            });
            foreach ((HttpStatusCode codigo, TimeSpan latencia) in await Task.WhenAll(pedidas))
            {
                codigosConsola.Add(codigo);
                latencias.Add(latencia);
            }
        }
        HttpResponseMessage[] respuestasPortal = await Task.WhenAll(portal);

        // QA-05 e: el 100 % de la consola atendida y dentro del objetivo.
        Assert.All(codigosConsola, c => Assert.Equal(HttpStatusCode.OK, c));
        Assert.All(latencias, l => Assert.True(l < ObjetivoConsola, $"La consola tardó {l.TotalMilliseconds} ms"));
        // El portal no recibe 503: lo que no cupo se respondió con la última respuesta, degradada.
        Assert.All(respuestasPortal, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        HttpResponseMessage[] degradadas = respuestasPortal
            .Where(r => r.Headers.TryGetValues(Priorizacion.EncabezadoDegradado, out var v) && v.Single() == "true").ToArray();
        Assert.True(degradadas.Length > 0, "Ninguna consulta del portal se degradó: la prueba no saturó el portal.");
        JsonElement cuerpo = await degradadas[0].Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(cuerpo.GetProperty("degradado").GetBoolean());
        Assert.Equal(1, cuerpo.GetProperty("peticion").GetInt32());   // es la respuesta guardada, no una nueva
        Assert.True(degradadas[0].Headers.Contains(Priorizacion.EncabezadoDatoAl));
        // Solo las que tuvieron permiso llegaron al servicio.
        Assert.True(_servicio.PeticionesConsulta < 81);
    }

    [Fact]
    public async Task Un_arreglo_JSON_degradado_conserva_su_forma_y_se_marca_en_el_encabezado()
    {
        _servicio.Demoras["/v1/consulta/mapa"] = TimeSpan.FromMilliseconds(400);
        string token = _tokens.Token("ciudadano", "organismo");
        await Pedir("/v1/consulta/mapa", token);

        HttpResponseMessage[] respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Pedir("/v1/consulta/mapa", token)));

        HttpResponseMessage degradada = respuestas.First(r => r.Headers.Contains(Priorizacion.EncabezadoDegradado));
        Assert.Equal(JsonValueKind.Array, (await degradada.Content.ReadFromJsonAsync<JsonElement>()).ValueKind);
    }

    [Fact]
    public async Task Sin_respuesta_guardada_o_fuera_de_las_consultas_la_saturacion_responde_503_con_Retry_After()
    {
        _servicio.Demoras["/v1/descarga"] = TimeSpan.FromMilliseconds(400);
        string evaluador = _tokens.Token("evaluador.armenia", "evaluador");

        HttpResponseMessage[] respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Pedir("/v1/descarga", evaluador)));

        HttpResponseMessage saturada = respuestas.First(r => r.StatusCode == HttpStatusCode.ServiceUnavailable);
        Assert.Equal("1", saturada.Headers.RetryAfter!.ToString());
        Assert.Contains(respuestas, r => r.StatusCode == HttpStatusCode.OK);
    }
}
