using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Comun.Seguridad;
using Testcontainers.Keycloak;

namespace Plataforma.Comun.Pruebas.Seguridad;

/// <summary>
/// Prueba de integración con <see cref="KeycloakContainer"/> (Testcontainers.Keycloak) importando
/// <c>despliegue/keycloak/realm-plataforma.json</c> (DA-15).
/// </summary>
public sealed class PruebasKeycloakIntegracion : IAsyncLifetime
{
    public const string ImagenKeycloak = "quay.io/keycloak/keycloak:26.5.2";
    private const string Emisor = "http://localhost:8180/realms/plataforma";
    private const string Audiencia = "gateway";

    private readonly KeycloakContainer _keycloak;

    public PruebasKeycloakIntegracion()
    {
        string rutaRealm = Path.Combine(RaizRepositorio(), "despliegue", "keycloak", "realm-plataforma.json");
        _keycloak = new KeycloakBuilder(ImagenKeycloak)
            .WithResourceMapping(new FileInfo(rutaRealm), new FileInfo("/opt/keycloak/data/import/realm-plataforma.json"))
            .WithCommand("--import-realm")
            .WithEnvironment("KC_HOSTNAME", "http://localhost:8180")
            .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
            .Build();
    }

    public Task InitializeAsync() => _keycloak.StartAsync();

    public Task DisposeAsync() => _keycloak.DisposeAsync().AsTask();

    [Fact]
    public async Task Token_por_password_grant_de_bomberos_operador_produce_la_identidad_esperada()
    {
        string urlBase = _keycloak.GetBaseAddress().TrimEnd('/');
        using var http = new HttpClient();

        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "movil",
            ["username"] = "bomberos.operador",
            ["password"] = "bomberos.operador_local",
        });

        HttpResponseMessage respuestaToken = await http.PostAsync(
            $"{urlBase}/realms/plataforma/protocol/openid-connect/token",
            formulario);
        string cuerpoRespuesta = await respuestaToken.Content.ReadAsStringAsync();
        Assert.True(respuestaToken.IsSuccessStatusCode, $"Keycloak rechazó el password grant: {cuerpoRespuesta}");

        using JsonDocument jsonRespuesta = JsonDocument.Parse(cuerpoRespuesta);
        string? accessToken = jsonRespuesta.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // Verificar en el payload del token el emisor fijo y la audiencia "gateway".
        string[] partes = accessToken!.Split('.');
        using JsonDocument payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(partes[1]));
        Assert.Equal(Emisor, payload.RootElement.GetProperty("iss").GetString());
        Assert.True(ContieneAudiencia(payload.RootElement.GetProperty("aud"), Audiencia));

        // Validar el token con IdentidadPorJwt descubriendo y cacheando las llaves desde los metadatos OIDC de Keycloak.
        var opciones = new OpcionesIdentidadJwt
        {
            Emisor = Emisor,
            Audiencia = Audiencia,
            Metadatos = $"{urlBase}/realms/plataforma/.well-known/openid-configuration",
        };
        var validador = new ValidadorJwt(opciones);

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {accessToken}";
        contexto.Request.Headers["X-Dispositivo"] = "disp-integracion-01";
        contexto.Request.Headers["X-Usuario"] = "impostor.ignorado";

        var adaptador = new IdentidadPorJwt(new HttpContextAccessor { HttpContext = contexto }, validador);
        Identidad? identidad = adaptador.Actual;

        Assert.NotNull(identidad);
        Assert.Equal("bomberos.operador", identidad.Usuario);
        Assert.Single(identidad.Roles, RolesPlataforma.Organismo);
        Assert.Equal("bomberos", identidad.Organizacion);
        Assert.Null(identidad.Ambito);
        Assert.Null(identidad.Territorio);
        Assert.Equal("disp-integracion-01", identidad.IdDispositivo);
    }

    private static bool ContieneAudiencia(JsonElement elementoAud, string esperada) =>
        elementoAud.ValueKind switch
        {
            JsonValueKind.String => elementoAud.GetString() == esperada,
            JsonValueKind.Array => elementoAud.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == esperada),
            _ => false,
        };

    private static string RaizRepositorio()
    {
        DirectoryInfo? carpeta = new(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Plataforma.sln")))
        {
            carpeta = carpeta.Parent;
        }
        return carpeta?.FullName ?? throw new DirectoryNotFoundException("No se encontró Plataforma.sln.");
    }
}
