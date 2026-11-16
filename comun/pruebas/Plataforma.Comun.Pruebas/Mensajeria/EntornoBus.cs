using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Comun.Mensajeria;
using Testcontainers.PostgreSql;

namespace Plataforma.Comun.Pruebas.Mensajeria;

/// <summary>Efecto observable de un consumidor de prueba: una fila por evento aplicado.</summary>
public sealed class Efecto
{
    public string Clave { get; set; } = "";
    public int Veces { get; set; }
}

public sealed class ContextoPrueba : DbContext
{
    public ContextoPrueba(DbContextOptions<ContextoPrueba> opciones) : base(opciones) { }

    public DbSet<Efecto> Efectos => Set<Efecto>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.ConfigurarBandejaSalida();
        modelo.ConfigurarMensajesProcesados();
        modelo.Entity<Efecto>(e =>
        {
            e.ToTable("efectos");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Clave).HasColumnName("clave");
            e.Property(x => x.Veces).HasColumnName("veces");
        });
    }
}

/// <summary>
/// PostgreSQL 18 y RabbitMQ 4.4 con la topología real de despliegue/bus/definiciones.json (un nodo).
/// Se levantan una vez para todas las pruebas de la colección.
/// </summary>
public sealed class EntornoBus : IAsyncLifetime
{
    public const string ImagenPostgres = "postgres:18.4";
    public const string ImagenRabbit = "rabbitmq:4.4.0-management";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(ImagenPostgres).Build();
    private readonly IContainer _rabbit;

    public EntornoBus()
    {
        string bus = Path.Combine(RaizRepositorio(), "despliegue", "bus");
        _rabbit = new ContainerBuilder(ImagenRabbit)
            .WithPortBinding(5672, true)
            .WithPortBinding(15672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "definiciones.json")), "/etc/rabbitmq/definiciones.json")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "rabbitmq-un-nodo.conf")), "/etc/rabbitmq/rabbitmq.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
    }

    public string CadenaBase => _postgres.GetConnectionString();
    public string NodoBus => $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";
    public Uri UriAdministracion => new($"http://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(15672)}/api/");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await using ContextoPrueba db = CrearContexto();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public ContextoPrueba CrearContexto() =>
        new(new DbContextOptionsBuilder<ContextoPrueba>().UseNpgsql(CadenaBase).Options);

    /// <summary>Opciones de un servicio. Las claves de desarrollo son "&lt;usuario&gt;_local" (generar_definiciones.py).</summary>
    public OpcionesBus Opciones(string usuario, string? cola = null, string? nodo = null) => new()
    {
        Nodos = [nodo ?? NodoBus],
        Usuario = usuario,
        Clave = usuario.Replace("-", "_") + "_local",
        Productor = usuario,
        Cola = cola,
        IntervaloRelevoMs = 100,
        TamanoLote = 50,
    };

    /// <summary>Proveedor de servicios como el de un servicio real: DbContext con ámbito y adaptador de entrada.</summary>
    public ServiceProvider Proveedor(OpcionesBus opciones, IManejadorEvento? manejador = null)
    {
        ServiceCollection servicios = new();
        servicios.AddLogging(l => l.AddProvider(NullLoggerProvider.Instance));
        servicios.AddDbContext<ContextoPrueba>(o => o.UseNpgsql(CadenaBase));
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<ConexionBus>();
        servicios.AddScoped<EscritorBandeja<ContextoPrueba>>();
        if (manejador is not null)
        {
            servicios.AddScoped(_ => manejador);
        }
        return servicios.BuildServiceProvider();
    }

    // ----------------------------------------------------------------- API de administración de RabbitMQ

    private HttpClient Administracion()
    {
        HttpClient cliente = new() { BaseAddress = UriAdministracion };
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin_local")));
        return cliente;
    }

    public async Task<long> MensajesEnColaAsync(string cola)
    {
        using HttpClient cliente = Administracion();
        // Las estadísticas de la API se refrescan cada pocos segundos: se espera a que se estabilicen.
        string json = await cliente.GetStringAsync($"queues/%2F/{Uri.EscapeDataString(cola)}");
        using var documento = System.Text.Json.JsonDocument.Parse(json);
        return documento.RootElement.TryGetProperty("messages", out var m) ? m.GetInt64() : 0;
    }

    public async Task VaciarColasAsync()
    {
        using HttpClient cliente = Administracion();
        string json = await cliente.GetStringAsync("queues/%2F");
        using var documento = System.Text.Json.JsonDocument.Parse(json);
        foreach (var cola in documento.RootElement.EnumerateArray())
        {
            string nombre = cola.GetProperty("name").GetString()!;
            await cliente.DeleteAsync($"queues/%2F/{Uri.EscapeDataString(nombre)}/contents");
        }
    }

    public async Task PonerPoliticaAsync(string nombre, string patron, string definicionJson, int prioridad)
    {
        using HttpClient cliente = Administracion();
        string cuerpo = $"{{\"pattern\":\"{patron}\",\"apply-to\":\"queues\",\"priority\":{prioridad},\"definition\":{definicionJson}}}";
        HttpResponseMessage respuesta = await cliente.PutAsync($"policies/%2F/{nombre}",
            new StringContent(cuerpo, Encoding.UTF8, "application/json"));
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task QuitarPoliticaAsync(string nombre)
    {
        using HttpClient cliente = Administracion();
        await cliente.DeleteAsync($"policies/%2F/{nombre}");
    }

    public async Task PublicarCrudoAsync(string claveRuteo, string cuerpo)
    {
        using HttpClient cliente = Administracion();
        string payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            properties = new { message_id = Guid.NewGuid().ToString() },
            routing_key = claveRuteo,
            payload = cuerpo,
            payload_encoding = "string",
        });
        HttpResponseMessage respuesta = await cliente.PostAsync("exchanges/%2F/plataforma.eventos/publish",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task LimpiarTablasAsync()
    {
        await using ContextoPrueba db = CrearContexto();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE bandeja_salida, contador_productor, mensajes_procesados, efectos RESTART IDENTITY");
    }

    public static async Task EsperarAsync(Func<Task<bool>> condicion, string descripcion, int segundos = 60)
    {
        DateTime limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
            {
                return;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"No se cumplió en {segundos} s: {descripcion}");
    }

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

[CollectionDefinition(Nombre)]
public sealed class ColeccionBus : ICollectionFixture<EntornoBus>
{
    public const string Nombre = "bus";
}
