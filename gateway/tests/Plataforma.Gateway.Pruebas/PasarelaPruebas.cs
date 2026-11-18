using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Plataforma.Gateway.Correlacion;

namespace Plataforma.Gateway.Pruebas;

/// <summary>Nivel 1 (DA-15), reenvío del mismo JWT, encabezados de identidad descartados y correlación (DA-20).</summary>
public class PasarelaPruebas : IAsyncLifetime
{
    private readonly EmisorTokens _tokens = new();
    private ServicioSimulado _servicio = null!;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _gateway = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        _servicio = await ServicioSimulado.IniciarAsync();
        _gateway = GatewayDePrueba.Crear(CatalogoDePrueba.Escribir(_servicio.Direccion), _tokens);
        _cliente = _gateway.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _gateway.DisposeAsync();
        await _servicio.DisposeAsync();
        _tokens.Dispose();
    }

    private static async Task<Dictionary<string, string>> EncabezadosRecibidos(HttpResponseMessage respuesta)
    {
        JsonElement eco = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        return eco.GetProperty("encabezados").Deserialize<Dictionary<string, string>>()!
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sin_token_responde_401_y_no_llega_al_servicio()
    {
        HttpResponseMessage r = await _cliente.SendAsync(GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("SIN_IDENTIDAD", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Un_token_de_otro_emisor_o_alterado_responde_401()
    {
        using EmisorTokens otro = new();
        HttpResponseMessage r = await _cliente.SendAsync(
            GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos", otro.Token("intruso", "autoridad")));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Con_un_rol_que_la_ruta_no_admite_responde_403()
    {
        HttpResponseMessage lectura = await _cliente.SendAsync(
            GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos", _tokens.Token("bomberos.operador", "organismo")));
        HttpResponseMessage modificacion = await _cliente.SendAsync(
            GatewayDePrueba.Peticion(HttpMethod.Post, "/v1/proyectos/1/avance", _tokens.Token("alcaldia.armenia", "autoridad")));

        Assert.Equal(HttpStatusCode.Forbidden, lectura.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, modificacion.StatusCode);
    }

    [Fact]
    public async Task Con_el_rol_correcto_reenvia_el_mismo_JWT_y_descarta_los_encabezados_de_identidad()
    {
        string token = _tokens.Token("alcaldia.armenia", "autoridad");
        HttpRequestMessage peticion = GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos", token);
        peticion.Headers.Add("X-Usuario", "otro");
        peticion.Headers.Add("X-Roles", "administrador");
        peticion.Headers.Add("X-Rol", "Autoridad");
        peticion.Headers.Add("X-Dispositivo", "tableta-7");

        HttpResponseMessage r = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Dictionary<string, string> recibidos = await EncabezadosRecibidos(r);
        Assert.Equal($"Bearer {token}", recibidos["Authorization"]);
        Assert.False(recibidos.ContainsKey("X-Usuario"));
        Assert.False(recibidos.ContainsKey("X-Roles"));
        Assert.False(recibidos.ContainsKey("X-Rol"));
        Assert.Equal("tableta-7", recibidos["X-Dispositivo"]);   // dato del dispositivo, no identidad
    }

    [Fact]
    public async Task Asigna_traceparent_y_X_Id_Correlacion_y_los_devuelve()
    {
        HttpResponseMessage r = await _cliente.SendAsync(
            GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos", _tokens.Token("auditor.1", "auditor")));

        Dictionary<string, string> recibidos = await EncabezadosRecibidos(r);
        Assert.True(CorrelacionPeticiones.EsTraceparent(recibidos["traceparent"]), recibidos["traceparent"]);
        string correlacion = recibidos[CorrelacionPeticiones.EncabezadoCorrelacion];
        Assert.Matches("^[0-9a-f]{32}$", correlacion);
        Assert.Equal(correlacion, r.Headers.GetValues(CorrelacionPeticiones.EncabezadoCorrelacion).Single());
    }

    [Fact]
    public async Task Conserva_la_correlacion_valida_del_cliente_y_reemplaza_la_invalida()
    {
        string token = _tokens.Token("auditor.1", "auditor");
        HttpRequestMessage valida = GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos", token);
        valida.Headers.Add(CorrelacionPeticiones.EncabezadoCorrelacion, "app-7f3a");
        HttpRequestMessage invalida = GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/proyectos", token);
        invalida.Headers.TryAddWithoutValidation(CorrelacionPeticiones.EncabezadoCorrelacion, "<script>");

        Dictionary<string, string> conValida = await EncabezadosRecibidos(await _cliente.SendAsync(valida));
        Dictionary<string, string> conInvalida = await EncabezadosRecibidos(await _cliente.SendAsync(invalida));

        Assert.Equal("app-7f3a", conValida[CorrelacionPeticiones.EncabezadoCorrelacion]);
        Assert.Matches("^[0-9a-f]{32}$", conInvalida[CorrelacionPeticiones.EncabezadoCorrelacion]);
    }

    [Fact]
    public async Task Una_ruta_de_la_API_que_no_esta_en_el_catalogo_no_existe()
    {
        HttpResponseMessage r = await _cliente.SendAsync(
            GatewayDePrueba.Peticion(HttpMethod.Get, "/v1/no-existe", _tokens.Token("auditor.1", "auditor")));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task La_consola_se_sirve_sin_token_pero_solo_para_leer()
    {
        HttpResponseMessage pagina = await _cliente.SendAsync(GatewayDePrueba.Peticion(HttpMethod.Get, "/proyectos/listado"));
        HttpResponseMessage escritura = await _cliente.SendAsync(GatewayDePrueba.Peticion(HttpMethod.Post, "/proyectos/listado"));

        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Equal("/proyectos/listado", (await pagina.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ruta").GetString());
        Assert.NotEqual(HttpStatusCode.OK, escritura.StatusCode);
    }

    [Fact]
    public async Task El_gateway_responde_sus_sondeos_de_salud()
    {
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/salud/vivo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/salud/listo")).StatusCode);
    }
}
