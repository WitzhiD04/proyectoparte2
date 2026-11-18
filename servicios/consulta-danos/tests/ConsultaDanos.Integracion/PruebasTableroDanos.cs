using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ConsultaDanos.Api.Tablero;
using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ConteoTipo = ConsultaDanos.Dominio.ConteoTipo;

namespace ConsultaDanos.Integracion;

/// <summary>
/// El tablero Razor (E-41) con los casos de uso reales sobre puertos en memoria. No usa contenedores: solo prueba el
/// segundo adaptador de entrada, su sesión y lo que presenta.
/// </summary>
public class PruebasTableroDanos
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 18, 44, 0, TimeSpan.Zero);
    private static readonly Guid Desastre = Guid.Parse("103ecf6b-35c9-4116-a4fd-c01209793dab");
    private const string RutaTablero = "/tablero-danos";

    private sealed class Puertos
    {
        public LecturasFalsas Lecturas { get; } = new();
        public CacheFalsa Cache { get; } = new();
        public DirectorioFalso Directorio { get; } = new();
    }

    private static WebApplicationFactory<Program> Fabrica(Puertos puertos, bool sesionDePrueba = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("ConnectionStrings:ConsultaDanos", "Host=sin-base;Database=x");
            b.UseSetting("Redis:Conexion", "sin-redis:6379");
            b.UseSetting("Bus:Nodos:0", "sin-bus");
            b.UseSetting("Bus:Clave", "x");
            b.UseSetting("Tablero:ClaveCliente", "clave-de-prueba");
            b.ConfigureLogging(l => l.ClearProviders());
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IHostedService>();
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<ILecturasDanos>();
                s.AddSingleton<ILecturasDanos>(puertos.Lecturas);
                s.RemoveAll<ICacheDanos>();
                s.AddSingleton<ICacheDanos>(puertos.Cache);
                s.RemoveAll<IDirectorioDesastres>();
                s.AddSingleton<IDirectorioDesastres>(puertos.Directorio);
                // Los metadatos de Keycloak en memoria: la redirección se arma sin red.
                s.PostConfigure<OpenIdConnectOptions>(RegistroTablero.EsquemaOidc, o =>
                {
                    OpenIdConnectConfiguration configuracion = new()
                    {
                        Issuer = "http://localhost:8180/realms/plataforma",
                        AuthorizationEndpoint = "http://localhost:8180/realms/plataforma/protocol/openid-connect/auth",
                        EndSessionEndpoint = "http://localhost:8180/realms/plataforma/protocol/openid-connect/logout",
                    };
                    o.Configuration = configuracion;
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuracion);
                });
                if (sesionDePrueba)
                {
                    s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SesionDePrueba>(SesionDePrueba.Esquema, _ => { });
                    s.PostConfigure<AuthenticationOptions>(a =>
                    {
                        a.DefaultScheme = SesionDePrueba.Esquema;
                        a.DefaultAuthenticateScheme = SesionDePrueba.Esquema;
                        a.DefaultChallengeScheme = SesionDePrueba.Esquema;
                        a.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    });
                }
            });
        });

    private static HttpClient Cliente(WebApplicationFactory<Program> fabrica, string? roles = "autoridad")
    {
        HttpClient cliente = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (roles is not null)
        {
            cliente.DefaultRequestHeaders.Add(SesionDePrueba.Encabezado, roles);
        }
        return cliente;
    }

    private static async Task<string> PaginaAsync(Puertos puertos, string ruta = RutaTablero, string roles = "autoridad")
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(puertos);
        HttpResponseMessage r = await Cliente(fabrica, roles).GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("autoridad")]
    [InlineData("organismo")]
    [InlineData("operador")]
    [InlineData("auditor")]
    public async Task Los_roles_del_tablero_lo_ven(string rol) =>
        Assert.Contains("Zonas en orden de prioridad", await PaginaAsync(new Puertos(), roles: rol));

    [Theory]
    [InlineData("evaluador")]
    [InlineData("responsable-proyecto")]
    [InlineData("administrador")]
    public async Task Otro_rol_va_a_la_pantalla_sin_acceso(string rol)
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(new Puertos());
        HttpResponseMessage r = await Cliente(fabrica, rol).GetAsync(RutaTablero);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/tablero-danos/sin-acceso", r.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_login_de_Keycloak_con_PKCE_y_regreso_por_GET()
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(new Puertos(), sesionDePrueba: false);
        HttpResponseMessage r = await Cliente(fabrica, null).GetAsync(RutaTablero);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        string destino = r.Headers.Location!.ToString();
        Assert.StartsWith("http://localhost:8180/realms/plataforma/protocol/openid-connect/auth?", destino);
        Assert.Contains("client_id=consulta-danos-tablero", destino);
        Assert.Contains("response_type=code", destino);
        Assert.DoesNotContain("response_mode=form_post", destino); // con response_type=code, omitirlo es regresar por GET
        Assert.Contains("code_challenge_method=S256", destino);
        Assert.Contains(Uri.EscapeDataString("/tablero-danos/signin-oidc"), destino);
    }

    [Fact]
    public async Task La_direccion_de_regreso_usa_el_host_publico_que_informa_el_gateway()
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(new Puertos(), sesionDePrueba: false);
        HttpClient cliente = Cliente(fabrica, null);
        cliente.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost:8080");
        cliente.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        string destino = (await cliente.GetAsync(RutaTablero)).Headers.Location!.ToString();

        Assert.Contains(Uri.EscapeDataString("http://localhost:8080/tablero-danos/signin-oidc"), destino);
    }

    [Fact]
    public async Task Un_host_reenviado_desconocido_no_llega_a_la_direccion_de_regreso()
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(new Puertos(), sesionDePrueba: false);
        HttpClient cliente = Cliente(fabrica, null);
        cliente.DefaultRequestHeaders.Add("X-Forwarded-Host", "atacante.example");

        string destino = (await cliente.GetAsync(RutaTablero)).Headers.Location!.ToString();

        Assert.DoesNotContain("atacante.example", destino);
    }

    [Fact]
    public async Task La_API_sigue_sin_usar_la_sesion_del_tablero()
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(new Puertos(), sesionDePrueba: false);
        HttpResponseMessage r = await Cliente(fabrica, null).GetAsync($"/v1/consulta-danos/consolidado?desastre={Desastre}");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Muestra_las_zonas_en_el_orden_de_la_priorizacion_con_su_distribucion()
    {
        string html = await PaginaAsync(new Puertos());

        Assert.Contains("Sismo del Quindío", html);
        Assert.True(html.IndexOf("ARMENIA", StringComparison.Ordinal) < html.IndexOf("CALARCÁ", StringComparison.Ordinal));
        Assert.Contains("QUINDÍO", html);
        Assert.Contains("0,75", html);
        Assert.Contains("Destrucción total", html);
        Assert.Contains("18/11/2026, 1:44 p. m.", html);
    }

    [Fact]
    public async Task Muestra_tipos_criticos_e_integraciones()
    {
        string html = await PaginaAsync(new Puertos());

        Assert.Contains("Tipos críticos:", html);
        Assert.Contains("Escuela", html);
        Assert.Contains("SE-6", html);
        Assert.Contains("No responde", html);
    }

    [Fact]
    public async Task Sin_conflictos_de_mas_no_hay_alarma()
    {
        string html = await PaginaAsync(new Puertos());
        Assert.Contains("Sin alarma", html);
        Assert.DoesNotContain("Alarma encendida", html);
    }

    [Fact]
    public async Task Con_la_bandeja_sobre_el_umbral_la_alarma_se_destaca()
    {
        Puertos puertos = new();
        puertos.Lecturas.Pendientes = 51;
        string html = await PaginaAsync(puertos);
        Assert.Contains("Alarma encendida", html);
        Assert.Contains("con-alarma", html);
        Assert.Contains("Hay más de 50 conflictos pendientes.", html);
    }

    [Fact]
    public async Task Si_la_base_cae_muestra_la_ultima_respuesta_y_avisa_que_esta_degradada()
    {
        Puertos puertos = new();
        await using WebApplicationFactory<Program> fabrica = Fabrica(puertos);
        HttpClient cliente = Cliente(fabrica);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync(RutaTablero)).StatusCode);

        puertos.Lecturas.FallaConsolidado = true;
        puertos.Cache.Version++;
        string html = WebUtility.HtmlDecode(await cliente.GetStringAsync(RutaTablero));

        Assert.Contains("Datos degradados:", html);
        Assert.Contains("ARMENIA", html);
    }

    [Fact]
    public async Task Sin_consolidado_ni_ultima_respuesta_explica_el_problema_sin_romper_la_pagina()
    {
        Puertos puertos = new();
        puertos.Lecturas.FallaConsolidado = true;
        string html = await PaginaAsync(puertos);
        Assert.Contains("Ni la base de consulta ni la caché tienen con qué responder.", html);
        Assert.Contains("Bandeja de revisión", html);
    }

    [Fact]
    public async Task Elige_el_desastre_de_la_direccion()
    {
        Puertos puertos = new();
        Guid otro = Guid.NewGuid();
        puertos.Directorio.Desastres.Add(new DesastreTablero(otro, "Inundación del Cauca", "REGISTRADO", T0));
        string html = await PaginaAsync(puertos, $"{RutaTablero}?desastre={otro}");
        Assert.Equal(otro, puertos.Lecturas.UltimoDesastre);
        Assert.Contains($"value=\"{otro}\" selected", html);
    }

    [Fact]
    public async Task Sin_la_lista_de_desastres_avisa_y_atiende_el_de_la_direccion()
    {
        Puertos puertos = new();
        puertos.Directorio.Falla = true;
        string html = await PaginaAsync(puertos, $"{RutaTablero}?desastre={Desastre}");
        Assert.Contains("No se pudo leer la lista de desastres", html);
        Assert.Contains("ARMENIA", html);
    }

    [Fact]
    public async Task La_hoja_de_estilos_es_publica_para_que_cargue_tambien_la_pantalla_de_error()
    {
        await using WebApplicationFactory<Program> fabrica = Fabrica(new Puertos(), sesionDePrueba: false);
        HttpResponseMessage r = await Cliente(fabrica, null).GetAsync("/tablero-danos/tablero.css");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("#12313f", await r.Content.ReadAsStringAsync());
    }

    private sealed class SesionDePrueba : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Esquema = "Prueba";
        public const string Encabezado = "X-Roles-Prueba";

        public SesionDePrueba(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? roles = Request.Headers[Encabezado].FirstOrDefault();
            if (roles is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            ClaimsIdentity identidad = new(Esquema, "preferred_username", RegistroTablero.ClaimRoles);
            identidad.AddClaim(new Claim("preferred_username", "usuario.prueba"));
            identidad.AddClaim(new Claim("name", "Usuario de prueba"));
            identidad.AddClaims(roles.Split(',').Select(r => new Claim(RegistroTablero.ClaimRoles, r)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema)));
        }
    }

    private sealed class DirectorioFalso : IDirectorioDesastres
    {
        public List<DesastreTablero> Desastres { get; } = [new(Desastre, "Sismo del Quindío", "EN_ATENCION", T0)];
        public bool Falla { get; set; }

        public Task<IReadOnlyList<DesastreTablero>> ListarAsync(CancellationToken ct) => Falla
            ? throw new HttpRequestException("Consulta no responde")
            : Task.FromResult<IReadOnlyList<DesastreTablero>>(Desastres);
    }

    private sealed class CacheFalsa : ICacheDanos
    {
        private readonly Dictionary<string, string> _valores = new();
        public long Version { get; set; }

        public Task<string?> LeerAsync(string clave, CancellationToken ct) => Task.FromResult(_valores.GetValueOrDefault(clave));
        public Task<IReadOnlyList<string?>> LeerVariasAsync(IReadOnlyList<string> claves, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string?>>(claves.Select(c => _valores.GetValueOrDefault(c)).ToList());
        public Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct)
        {
            _valores[clave] = valor;
            return Task.CompletedTask;
        }
        public Task<(long Global, long Desastre)> VersionesAsync(Guid desastre, CancellationToken ct) => Task.FromResult((Version, 0L));
        public Task InvalidarAsync(IReadOnlyCollection<Guid> desastres, bool global, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class LecturasFalsas : ILecturasDanos
    {
        public int Pendientes { get; set; } = 2;
        public bool FallaConsolidado { get; set; }
        public Guid? UltimoDesastre { get; private set; }

        public Task<IReadOnlyList<ZonaConsolidada>> ConsolidadoAsync(Guid desastre, CancellationToken ct)
        {
            UltimoDesastre = desastre;
            if (FallaConsolidado)
            {
                throw new InvalidOperationException("La base no responde");
            }
            IReadOnlyList<ZonaConsolidada> zonas =
            [
                new("63130", "CALARCÁ", "63", [new ConteoTipo("VIVIENDA", "Vivienda", NivelDano.LEVE, 3)]),
                new("63001", "ARMENIA", "63", [new ConteoTipo("ESCUELA", "Escuela", NivelDano.GRAVE, 2)]),
            ];
            return Task.FromResult(zonas);
        }

        public Task<DateTimeOffset?> DatoAlAsync(Guid desastre, CancellationToken ct) => Task.FromResult<DateTimeOffset?>(T0);
        public Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IntegracionLeida>>([new IntegracionLeida("SE-6", "ABIERTO", T0, 12)]);
        public Task<BandejaLeida> BandejaAsync(CancellationToken ct) =>
            Task.FromResult(new BandejaLeida(Pendientes, T0.AddHours(-3), T0));

        public Task<IReadOnlyList<ZonaMapa>> ZonasMapaAsync(FiltroMapa filtro, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<PuntoMapa>> PuntosAsync(FiltroMapa filtro, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, string>> GeometriasAsync(IReadOnlyCollection<string> codigos, CancellationToken ct) => throw new NotImplementedException();
        public Task<EvolucionActivo?> EvolucionAsync(Guid idActivo, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> NombreMunicipioAsync(string codigo, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> DepartamentoEnCapaAsync(string codigo, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<EntradaHistorial>> HistorialZonaAsync(string codigo, TipoZona tipo, Guid? desastre, CancellationToken ct) => throw new NotImplementedException();
    }
}
