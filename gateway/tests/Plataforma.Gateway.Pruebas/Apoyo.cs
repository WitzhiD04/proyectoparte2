using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Comun.Seguridad;

namespace Plataforma.Gateway.Pruebas;

/// <summary>
/// Servicio simulado en Kestrel (puerto libre): responde con lo que recibió (encabezados), con una demora configurable
/// por ruta, y cuenta las peticiones. Hace de todos los servicios y de la consola.
/// </summary>
public sealed class ServicioSimulado : IAsyncDisposable
{
    private readonly WebApplication _app;
    public ConcurrentDictionary<string, TimeSpan> Demoras { get; } = new(StringComparer.Ordinal);
    public int PeticionesConsulta;

    private ServicioSimulado(WebApplication app) => _app = app;

    public string Direccion { get; private set; } = "";

    public static async Task<ServicioSimulado> IniciarAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        ServicioSimulado simulado = new(app);
        app.Use(async (contexto, siguiente) =>
        {
            if (simulado.Demoras.TryGetValue(contexto.Request.Path.Value ?? "", out TimeSpan demora))
            {
                await Task.Delay(demora);
            }
            await siguiente(contexto);
        });
        app.MapGet("/v1/consulta/desastres", () =>
        {
            int n = Interlocked.Increment(ref simulado.PeticionesConsulta);
            return Results.Json(new { desastres = new[] { "sismo" }, peticion = n });
        });
        app.MapGet("/v1/consulta/mapa", () => Results.Json(new[] { 1, 2, 3 }));
        app.Map("/{**ruta}", (HttpContext contexto) => Results.Json(new
        {
            metodo = contexto.Request.Method,
            ruta = contexto.Request.Path.Value,
            encabezados = contexto.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
        }));
        await app.StartAsync();
        simulado.Direccion = app.Urls.First().TrimEnd('/') + "/";
        return simulado;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}

/// <summary>Emite tokens como los de Keycloak (realm_access.roles, organizacion, ambito, territorio).</summary>
public sealed class EmisorTokens : IDisposable
{
    public const string Emisor = "http://localhost:8180/realms/plataforma";
    public const string Audiencia = "gateway";
    private readonly RSA _rsa = RSA.Create(2048);

    public EmisorTokens() => Llave = new RsaSecurityKey(_rsa) { KeyId = "llave-pruebas" };

    public RsaSecurityKey Llave { get; }

    public string Token(string usuario, params string[] roles)
    {
        DateTime ahora = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emisor,
            Audience = Audiencia,
            NotBefore = ahora.AddMinutes(-1),
            IssuedAt = ahora.AddMinutes(-1),
            Expires = ahora.AddMinutes(10),
            Claims = new Dictionary<string, object>
            {
                ["preferred_username"] = usuario,
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles },
                ["organizacion"] = "pruebas",
            },
            SigningCredentials = new SigningCredentials(Llave, SecurityAlgorithms.RsaSha256),
        });
    }

    public void Dispose() => _rsa.Dispose();
}

/// <summary>Arma un catálogo de prueba que apunta todas las rutas al servicio simulado.</summary>
public static class CatalogoDePrueba
{
    public static string Escribir(string direccion, Action<JsonObject>? ajustar = null)
    {
        JsonObject Ruta(string ruta, string[] metodos, string roles, string clase, string? regla = "ninguna")
        {
            JsonObject metadatos = new() { ["servicio"] = "simulado", ["roles"] = roles, ["claseTrafico"] = clase };
            if (regla is not null)
            {
                metadatos["reglaDominio"] = regla;
            }
            return new JsonObject
            {
                ["ClusterId"] = "simulado",
                ["Match"] = new JsonObject { ["Path"] = ruta, ["Methods"] = new JsonArray(metodos.Select(m => (JsonNode)m).ToArray()) },
                ["Metadata"] = metadatos,
            };
        }

        JsonObject rutas = new()
        {
            ["proyectos"] = Ruta("/v1/proyectos", ["GET"], "autoridad,auditor", "consola"),
            ["avance"] = Ruta("/v1/proyectos/{id}/avance", ["POST"], "responsable-proyecto", "consola", "responsable-asignado"),
            ["consulta-desastres"] = Ruta("/v1/consulta/desastres", ["GET"], "*", "portal"),
            ["consulta-mapa"] = Ruta("/v1/consulta/mapa", ["GET"], "*", "portal"),
            ["descarga"] = Ruta("/v1/descarga", ["GET"], "evaluador", "movil"),
            ["consola"] = Ruta("/{**ruta}", ["GET", "HEAD"], "publico", "consola"),
        };
        JsonObject documento = new()
        {
            ["ReverseProxy"] = new JsonObject
            {
                ["Routes"] = rutas,
                ["Clusters"] = new JsonObject
                {
                    ["simulado"] = new JsonObject
                    {
                        ["Destinations"] = new JsonObject { ["d1"] = new JsonObject { ["Address"] = direccion } },
                    },
                },
            },
        };
        ajustar?.Invoke(rutas);
        string archivo = Path.Combine(Path.GetTempPath(), $"rutas-prueba-{Guid.NewGuid():N}.json");
        File.WriteAllText(archivo, documento.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return archivo;
    }
}

/// <summary>Gateway en memoria (TestServer) con el catálogo y los parámetros que pida la prueba.</summary>
public static class GatewayDePrueba
{
    public static WebApplicationFactory<Program> Crear(string catalogo, EmisorTokens tokens,
        IDictionary<string, string>? ajustes = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(Environments.Production);
            b.UseSetting("Gateway:Catalogo", catalogo);
            foreach ((string clave, string valor) in ajustes ?? new Dictionary<string, string>())
            {
                b.UseSetting(clave, valor);
            }
            b.ConfigureTestServices(s => s.AddSingleton(new OpcionesIdentidadJwt
            {
                Emisor = EmisorTokens.Emisor,
                Audiencia = EmisorTokens.Audiencia,
                LlavesFirma = [tokens.Llave],
            }));
        });

    public static HttpRequestMessage Peticion(HttpMethod metodo, string ruta, string? token = null)
    {
        HttpRequestMessage peticion = new(metodo, ruta);
        if (token is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return peticion;
    }
}
