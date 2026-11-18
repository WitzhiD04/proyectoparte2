using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Plataforma.Comun.Seguridad;
using StackExchange.Redis;

namespace ConsultaDanos.Api.Tablero;

public sealed class OpcionesTablero
{
    public string ClienteId { get; set; } = "consulta-danos-tablero";
    public string ClaveCliente { get; set; } = "";

    /// <summary>Nombres públicos del gateway, sin puerto, que se aceptan en <c>X-Forwarded-Host</c> para la dirección de regreso.</summary>
    public string[] HostsPublicos { get; set; } = ["localhost"];

    public string UrlConsulta { get; set; } = "http://consulta:8080/";
}

/// <summary>
/// Segundo adaptador de entrada del servicio (E-41): páginas Razor con sesión propia sobre los mismos casos de uso de la API.
/// Todo su cableado queda aquí y solo actúa bajo <see cref="RutaBase"/>; las rutas <c>/v1</c> siguen usando el JWT.
/// </summary>
public static class RegistroTablero
{
    public const string RutaBase = "/tablero-danos";
    public const string EsquemaOidc = OpenIdConnectDefaults.AuthenticationScheme;
    public const string PoliticaVer = "VerTableroDanos";
    public const string ClaimRoles = "roles";

    private const string ClaveLlaves = "consulta-danos:tablero:llaves-proteccion";
    private static readonly string[] RolesConAcceso =
    [
        RolesPlataforma.Autoridad, RolesPlataforma.Organismo, RolesPlataforma.Operador, RolesPlataforma.Auditor,
    ];

    public static IServiceCollection AgregarTablero(this IServiceCollection servicios, IConfiguration configuracion)
    {
        OpcionesTablero opciones = configuracion.GetSection("Tablero").Get<OpcionesTablero>() ?? new OpcionesTablero();
        servicios.AddSingleton(opciones);

        servicios.AddRazorPages(paginas =>
        {
            paginas.Conventions.AuthorizePage("/TableroDanos/Index", PoliticaVer);
            paginas.Conventions.AuthorizePage("/TableroDanos/SinAcceso");
            paginas.Conventions.AllowAnonymousToPage("/TableroDanos/Salir");
            paginas.Conventions.AllowAnonymousToPage("/TableroDanos/ErrorIngreso");
        });

        // Las dos réplicas deben poder abrir la cookie de la otra: comparten las llaves en Redis. Si se pierden, la sesión
        // vence y el inicio de sesión único de Keycloak la rehace sin pedir la clave.
        servicios.AddDataProtection().SetApplicationName("consulta-danos-tablero");
        servicios.AddOptions<KeyManagementOptions>().Configure<IConnectionMultiplexer>((llaves, redis) =>
            llaves.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), ClaveLlaves));

        // Los mismos valores con que los servicios validan el JWT: el emisor público y los metadatos por la red interna.
        // Con KC_HOSTNAME_BACKCHANNEL_DYNAMIC, esos metadatos dan el token y las llaves en keycloak:8080 y la pantalla de
        // ingreso en el host público, así que el navegador y el servidor llegan cada uno por su lado sin reescribir nada.
        string emisor = configuracion[$"{RegistroSeguridad.Seccion}:Emisor"] ?? OpcionesIdentidadJwt.EmisorPorOmision;
        string metadatos = configuracion[$"{RegistroSeguridad.Seccion}:Metadatos"] ?? OpcionesIdentidadJwt.MetadatosPorOmision;

        servicios.AddAuthentication(a =>
            {
                a.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                a.DefaultChallengeScheme = EsquemaOidc;
            })
            .AddCookie(c =>
            {
                c.Cookie.Name = "tablero-danos";
                c.Cookie.Path = RutaBase;
                c.Cookie.HttpOnly = true;
                c.Cookie.SameSite = SameSiteMode.Lax;
                c.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                c.ExpireTimeSpan = TimeSpan.FromHours(1);
                c.SlidingExpiration = true;
                c.AccessDeniedPath = $"{RutaBase}/sin-acceso";
            })
            .AddOpenIdConnect(EsquemaOidc, o =>
            {
                o.Authority = emisor;
                o.MetadataAddress = metadatos;
                o.RequireHttpsMetadata = metadatos.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                o.ClientId = opciones.ClienteId;
                o.ClientSecret = opciones.ClaveCliente;
                o.ResponseType = OpenIdConnectResponseType.Code;
                // El retorno llega por GET: el catálogo del gateway no admite una ruta pública de modificación (E-41).
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.SaveTokens = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.MapInboundClaims = false;
                o.CallbackPath = $"{RutaBase}/signin-oidc";
                o.SignedOutCallbackPath = $"{RutaBase}/signout-callback-oidc";
                o.SignedOutRedirectUri = RutaBase;
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = ClaimRoles;
                // Por omisión son SameSite=None y Secure, que el navegador puede descartar en http.
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnRedirectToIdentityProviderForSignOut = contexto =>
                {
                    // El id_token no se guarda en la cookie: Keycloak identifica el cliente por este parámetro.
                    contexto.ProtocolMessage.ClientId = opciones.ClienteId;
                    return Task.CompletedTask;
                };
                o.Events.OnRemoteFailure = contexto =>
                {
                    contexto.Response.Redirect($"{RutaBase}/error-ingreso");
                    contexto.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        servicios.AddAuthorizationBuilder().AddPolicy(PoliticaVer, p => p.RequireAuthenticatedUser().RequireRole(RolesConAcceso));

        servicios.AddSingleton<TokenServicioTablero>();
        servicios.AddHttpClient<TokenServicioTablero>(c => c.Timeout = TimeSpan.FromSeconds(3));
        servicios.AddHttpClient<IDirectorioDesastres, DirectorioDesastresConsulta>(c =>
        {
            c.BaseAddress = new Uri(opciones.UrlConsulta);
            c.Timeout = TimeSpan.FromSeconds(3);
        });
        return servicios;
    }

    public static WebApplication UsarTablero(this WebApplication app)
    {
        OpcionesTablero opciones = app.Services.GetRequiredService<OpcionesTablero>();
        ForwardedHeadersOptions reenviados = new()
        {
            // El servicio solo se alcanza por el gateway (no publica puertos) y además el host se limita a los conocidos.
            ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
            AllowedHosts = opciones.HostsPublicos,
        };
        reenviados.KnownIPNetworks.Clear();
        reenviados.KnownProxies.Clear();

        app.UseRouting();
        app.UseWhen(c => c.Request.Path.StartsWithSegments(RutaBase), tablero =>
        {
            tablero.UseForwardedHeaders(reenviados);
            tablero.UseStaticFiles();
            tablero.UseAuthentication();
            tablero.UseAuthorization();
        });
        app.MapRazorPages();
        return app;
    }
}
