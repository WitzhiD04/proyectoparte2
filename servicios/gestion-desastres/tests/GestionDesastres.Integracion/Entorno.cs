using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GestionDesastres.Aplicacion;
using GestionDesastres.Infraestructura;
using GestionDesastres.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GestionDesastres.Integracion;

/// <summary>
/// PostgreSQL 18 con PostGIS y RabbitMQ 4.4 con la topología real, el esquema aplicado por el migrador, la capa de
/// municipios de datos/geo cargada y el servicio levantado en memoria con su relevo y su consumidor.
/// </summary>
public sealed class Entorno : IAsyncLifetime
{
    public const string ClaveApp = "gestion_desastres_prueba";
    // Municipios para las pruebas: Armenia, Calarcá (Quindío), La Virginia y Pereira (Risaralda).
    public static readonly string[] Municipios = ["63001", "63130", "66400", "66001"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:18-3.6").WithDatabase("gestion_desastres").Build();
    private readonly IContainer _rabbit;
    private WebApplicationFactory<Program>? _fabrica;

    public Entorno()
    {
        string bus = Path.Combine(Raiz(), "despliegue", "bus");
        _rabbit = new ContainerBuilder("rabbitmq:4.4.0-management")
            .WithPortBinding(5672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "definiciones.json")), "/etc/rabbitmq/definiciones.json")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "rabbitmq-un-nodo.conf")), "/etc/rabbitmq/rabbitmq.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
    }

    public string Dueno => _postgres.GetConnectionString();
    public string App => new NpgsqlConnectionStringBuilder(Dueno) { Username = "gestion_desastres_app", Password = ClaveApp }.ConnectionString;
    public HttpClient Cliente { get; private set; } = null!;
    public IServiceProvider Servicios => _fabrica!.Services;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        IConfiguration migracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:GestionDesastresDueno"] = Dueno,
            ["Migracion:ClaveGestionDesastresApp"] = ClaveApp,
        }).Build();
        await MigracionGestionDesastres.EjecutarAsync(migracion, NullLogger.Instance);
        await CargarCapaAsync();

        string nodo = $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:GestionDesastres", App);
            b.UseSetting("Bus:Nodos:0", nodo);
            b.UseSetting("Bus:Usuario", "gestion-desastres");
            b.UseSetting("Bus:Clave", "gestion_desastres_local");
            b.UseSetting("Bus:IntervaloRelevoMs", "200");
        });
        Cliente = _fabrica.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public GestionDesastresDbContext ContextoApp() =>
        new(new DbContextOptionsBuilder<GestionDesastresDbContext>().UseNpgsql(App, n => n.UseNetTopologySuite()).Options);

    /// <summary>Carga la parte de la capa de municipios que usan las pruebas, como lo haría EV-21 (con el rol de la aplicación).</summary>
    private async Task CargarCapaAsync()
    {
        using JsonDocument capa = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Raiz(), "datos", "geo", "municipios-colombia.geojson")));
        List<DivisionGeografica> divisiones = capa.RootElement.GetProperty("features").EnumerateArray()
            .Where(f => Municipios.Contains(f.GetProperty("properties").GetProperty("codigo").GetString()))
            .Select(f => new DivisionGeografica(f.GetProperty("properties").GetProperty("codigo").GetString()!,
                f.GetProperty("properties").GetProperty("nombre").GetString()!, f.GetProperty("properties").GetProperty("codigoDepartamento").GetString(),
                f.GetProperty("geometry").GetRawText()))
            .ToList();
        await using GestionDesastresDbContext db = ContextoApp();
        await new CapaMunicipiosPostgis(db).AplicarAsync(divisiones, 1);
        await db.SaveChangesAsync();
    }

    /// <summary>Una petición con la identidad de transición (encabezados, hasta el gateway).</summary>
    public HttpRequestMessage Peticion(HttpMethod metodo, string ruta, object? cuerpo = null, string organizacion = "ungrd",
        string roles = "autoridad", string? ambito = null, string usuario = "usuario.prueba")
    {
        HttpRequestMessage m = new(metodo, ruta);
        m.Headers.Add("X-Usuario", usuario);
        m.Headers.Add("X-Roles", roles);
        m.Headers.Add("X-Organizacion", organizacion);
        if (ambito is not null)
        {
            m.Headers.Add("X-Ambito", ambito);
        }
        if (cuerpo is not null)
        {
            m.Content = JsonContent.Create(cuerpo);
        }
        return m;
    }

    public static async Task EsperarAsync(Func<Task<bool>> condicion, string descripcion, int segundos = 30)
    {
        DateTime limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicion()) return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"No se cumplió en {segundos} s: {descripcion}");
    }

    private static string Raiz()
    {
        DirectoryInfo? carpeta = new(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Plataforma.sln")))
        {
            carpeta = carpeta.Parent;
        }
        return carpeta?.FullName ?? throw new DirectoryNotFoundException("No se encontró Plataforma.sln.");
    }
}
