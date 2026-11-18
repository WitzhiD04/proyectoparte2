using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Plataforma.Gateway.Catalogo;

namespace Plataforma.Gateway.Pruebas;

/// <summary>El catálogo de rutas (DA-12) y la mitigación de R-07: sin regla de dominio, el gateway no arranca.</summary>
public class CatalogoPruebas
{
    private static string RutaCatalogoReal()
    {
        // El rutas.json del proyecto, el que se despliega.
        string? carpeta = AppContext.BaseDirectory;
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta, "gateway", "src", "Plataforma.Gateway", "rutas.json")))
        {
            carpeta = Path.GetDirectoryName(carpeta);
        }
        return Path.Combine(carpeta ?? throw new InvalidOperationException("No se encontró el repositorio."),
            "gateway", "src", "Plataforma.Gateway", "rutas.json");
    }

    private static IConfiguration Leer(string archivo) => new ConfigurationBuilder().AddJsonFile(archivo).Build();

    [Fact]
    public void El_catalogo_que_se_despliega_es_valido()
    {
        Assert.Empty(CatalogoRutas.Validar(Leer(RutaCatalogoReal())));
    }

    [Fact]
    public void Toda_ruta_de_modificacion_del_catalogo_real_declara_su_regla_de_dominio()
    {
        IConfiguration catalogo = Leer(RutaCatalogoReal());
        List<string> modificaciones = new();
        foreach (IConfigurationSection ruta in catalogo.GetSection("ReverseProxy:Routes").GetChildren())
        {
            string[] metodos = ruta.GetSection("Match:Methods").GetChildren().Select(m => m.Value!).ToArray();
            if (metodos.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE"))
            {
                modificaciones.Add(ruta.Key);
                Assert.False(string.IsNullOrWhiteSpace(ruta["Metadata:reglaDominio"]), ruta.Key);
            }
        }
        Assert.Equal(25, modificaciones.Count);
    }

    [Fact]
    public void El_tablero_entra_sin_token_y_solo_por_GET()
    {
        // La sesión del tablero es su cookie (E-41): el gateway no puede exigir un Bearer que el navegador no manda.
        IConfigurationSection ruta = Leer(RutaCatalogoReal()).GetSection("ReverseProxy:Routes:cd-tablero");

        Assert.Equal("/tablero-danos/{**resto}", ruta["Match:Path"]);
        Assert.Equal(["GET"], ruta.GetSection("Match:Methods").GetChildren().Select(m => m.Value));
        Assert.Equal(CatalogoRutas.Publico, ruta["Metadata:roles"]);
        Assert.Equal("consulta-danos", ruta["ClusterId"]);
    }

    [Fact]
    public void Una_ruta_de_modificacion_sin_regla_hace_invalido_el_catalogo()
    {
        string archivo = CatalogoDePrueba.Escribir("http://127.0.0.1:1/",
            rutas => ((JsonObject)rutas["avance"]!["Metadata"]!).Remove("reglaDominio"));

        IReadOnlyList<string> errores = CatalogoRutas.Validar(Leer(archivo));

        Assert.Contains(errores, e => e.Contains("avance") && e.Contains("reglaDominio"));
    }

    [Fact]
    public void Una_ruta_sin_metodos_cuenta_como_de_modificacion()
    {
        string archivo = CatalogoDePrueba.Escribir("http://127.0.0.1:1/", rutas =>
        {
            JsonObject proyectos = (JsonObject)rutas["proyectos"]!;
            ((JsonObject)proyectos["Match"]!).Remove("Methods");
            ((JsonObject)proyectos["Metadata"]!).Remove("reglaDominio");
        });

        Assert.Contains(CatalogoRutas.Validar(Leer(archivo)), e => e.Contains("proyectos") && e.Contains("todos los métodos"));
    }

    [Theory]
    [InlineData("roles", "jefe", "no existe en el realm")]
    [InlineData("claseTrafico", "urgente", "consola, movil o portal")]
    public void Los_metadatos_se_validan(string metadato, string valor, string error)
    {
        string archivo = CatalogoDePrueba.Escribir("http://127.0.0.1:1/",
            rutas => rutas["proyectos"]!["Metadata"]![metadato] = valor);
        Assert.Contains(CatalogoRutas.Validar(Leer(archivo)), e => e.Contains(error));
    }

    [Fact]
    public void El_gateway_no_arranca_con_una_ruta_de_modificacion_sin_regla()
    {
        using EmisorTokens tokens = new();
        string archivo = CatalogoDePrueba.Escribir("http://127.0.0.1:1/",
            rutas => ((JsonObject)rutas["avance"]!["Metadata"]!).Remove("reglaDominio"));
        using var gateway = GatewayDePrueba.Crear(archivo, tokens);

        Exception e = Assert.ThrowsAny<Exception>(() => gateway.CreateClient());

        Assert.IsType<CatalogoInvalidoException>(e.GetBaseException());
    }
}
