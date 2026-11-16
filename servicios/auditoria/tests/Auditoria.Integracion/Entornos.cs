using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Auditoria.Aplicacion;
using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Aplicacion.Puertos;
using Auditoria.Infraestructura.Mensajeria;
using Auditoria.Infraestructura.Persistencia;
using Auditoria.Infraestructura.Procesos;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Plataforma.Comun.Mensajeria;
using Testcontainers.PostgreSql;

namespace Auditoria.Integracion;

/// <summary>Reloj que la prueba mueve a voluntad.</summary>
public sealed class RelojDePrueba : IReloj
{
    public RelojDePrueba(DateTimeOffset ahora) { Ahora = ahora; }
    public DateTimeOffset Ahora { get; set; }
}

/// <summary>Migra la base de Auditoría como lo hace el migrador del despliegue (DT-19).</summary>
public static class Migracion
{
    public const string ClaveApp = "auditoria_prueba";

    public static Task MigrarAsync(string dueno) => MigracionAuditoria.EjecutarAsync(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AuditoriaDueno"] = dueno,
            ["Migracion:ClaveAuditoriaApp"] = ClaveApp,
        }).Build(), NullLogger.Instance);

    public static string Base(string servidor, string baseDatos, string? usuario = null, string? clave = null)
    {
        NpgsqlConnectionStringBuilder b = new(servidor) { Database = baseDatos };
        if (usuario is not null)
        {
            b.Username = usuario;
            b.Password = clave;
        }
        return b.ConnectionString;
    }
}

/// <summary>
/// PostgreSQL 18 con la base de Auditoría migrada. La base del servicio no existe antes de migrar, igual que en el
/// despliegue (el servidor compartido arranca con otra base).
/// </summary>
public sealed class BaseMigrada : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(EntornoAuditoria.ImagenPostgres).Build();

    public string Servidor => _postgres.GetConnectionString();
    public string Dueno => Migracion.Base(Servidor, MigracionAuditoria.Base);
    public string App => Migracion.Base(Servidor, MigracionAuditoria.Base, MigracionAuditoria.RolApp, Migracion.ClaveApp);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await Migracion.MigrarAsync(Dueno);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public AuditoriaDbContext ComoApp() => new(new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql(App).Options);

    /// <summary>Servicios como los del proceso real, sin bus ni procesos en segundo plano, con un reloj de prueba.</summary>
    public ServiceProvider Proveedor(IReloj reloj, OpcionesAuditoria? opciones = null)
    {
        opciones ??= new OpcionesAuditoria();
        ServiceCollection s = new();
        s.AddLogging(l => l.AddProvider(NullLoggerProvider.Instance));
        s.AddDbContext<AuditoriaDbContext>(o => o.UseNpgsql(App));
        s.AddSingleton(opciones);
        s.AddSingleton(opciones.Huecos);
        s.AddSingleton(reloj);
        s.AddScoped<RegistroAuditoriaEf>();
        s.AddScoped<IRegistroAuditoria>(p => p.GetRequiredService<RegistroAuditoriaEf>());
        s.AddScoped<ISecuenciasRegistradas>(p => p.GetRequiredService<RegistroAuditoriaEf>());
        s.AddScoped<ISeguimientos, SeguimientosEf>();
        s.AddSingleton<IAvisosHuecos, AvisosHuecosPorLog>();
        s.AddScoped<IRegistrarEventoEntrada, RegistrarEvento>();
        s.AddScoped<IRevisarSecuenciasEntrada, RevisarSecuencias>();
        s.AddScoped<IConsultarRegistroEntrada, ConsultarRegistro>();
        s.AddScoped<IConsultarHuecosEntrada, ConsultarHuecos>();
        s.AddScoped<IManejadorEvento, ManejadorEventosAuditoria>();
        s.AddSingleton<ProcesoRevisionHuecos>();
        s.AddSingleton(new OpcionesBus { Cola = "q.auditoria", Usuario = "auditoria", Productor = "auditoria" });
        s.AddSingleton<ConexionBus>();
        s.AddSingleton<ConsumidorIdempotente<AuditoriaDbContext>>();
        return s.BuildServiceProvider();
    }
}

/// <summary>Bandeja de salida de un productor de prueba: la misma tabla y el mismo relevo que usan los servicios reales.</summary>
public sealed class ContextoProductor : DbContext
{
    public ContextoProductor(DbContextOptions<ContextoProductor> opciones) : base(opciones) { }

    protected override void OnModelCreating(ModelBuilder modelo) => modelo.ConfigurarBandejaSalida();
}

/// <summary>Un productor de la plataforma simulado: su base con la bandeja de salida y su relevo hacia el bus.</summary>
public sealed class Productor : IAsyncDisposable
{
    private readonly ServiceProvider _servicios;

    public Productor(string nombre, string cadena, OpcionesBus opciones)
    {
        Nombre = nombre;
        Cadena = cadena;
        ServiceCollection s = new();
        s.AddLogging(l => l.AddProvider(NullLoggerProvider.Instance));
        s.AddDbContext<ContextoProductor>(o => o.UseNpgsql(cadena));
        s.AddSingleton(opciones);
        s.AddSingleton<ConexionBus>();
        s.AddScoped<EscritorBandeja<ContextoProductor>>();
        s.AddSingleton<RelevoBandeja<ContextoProductor>>();
        _servicios = s.BuildServiceProvider();
    }

    public string Nombre { get; }
    public string Cadena { get; }
    public RelevoBandeja<ContextoProductor> Relevo => _servicios.GetRequiredService<RelevoBandeja<ContextoProductor>>();

    public ContextoProductor Contexto() => new(new DbContextOptionsBuilder<ContextoProductor>().UseNpgsql(Cadena).Options);

    /// <summary>Escribe los eventos en la bandeja en una transacción, como lo hace un caso de uso (AD-45).</summary>
    public async Task EscribirAsync(params Plataforma.Comun.Eventos.Sobre[] sobres)
    {
        using IServiceScope ambito = _servicios.CreateScope();
        EscritorBandeja<ContextoProductor> escritor = ambito.ServiceProvider.GetRequiredService<EscritorBandeja<ContextoProductor>>();
        foreach (Plataforma.Comun.Eventos.Sobre sobre in sobres)
        {
            escritor.Agregar(sobre);
        }
        await ambito.ServiceProvider.GetRequiredService<ContextoProductor>().SaveChangesAsync();
    }

    /// <summary>Numera y publica todo lo pendiente.</summary>
    public async Task PublicarTodoAsync()
    {
        while (await Relevo.NumerarAsync(CancellationToken.None) > 0) { }
        while ((await Relevo.PublicarAsync(CancellationToken.None)).Publicados > 0) { }
    }

    public async ValueTask DisposeAsync() => await _servicios.DisposeAsync();
}

/// <summary>
/// PostgreSQL 18 y RabbitMQ 4.4 con la topología real de despliegue/bus/definiciones.json (un nodo), la base de
/// Auditoría migrada y una base con bandeja de salida por cada productor del catálogo.
/// </summary>
public sealed class EntornoAuditoria : IAsyncLifetime
{
    public const string ImagenPostgres = "postgres:18.4";
    public const string ImagenRabbit = "rabbitmq:4.4.0-management";

    public static readonly string[] Productores =
        ["gestion-desastres", "evaluacion-danos", "reconstruccion", "adaptador-se2", "adaptador-se7"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(ImagenPostgres).Build();
    private readonly IContainer _rabbit;
    private readonly Dictionary<string, Productor> _productores = new();

    public EntornoAuditoria()
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

    public string Servidor => _postgres.GetConnectionString();
    public string Dueno => Migracion.Base(Servidor, MigracionAuditoria.Base);
    public string App => Migracion.Base(Servidor, MigracionAuditoria.Base, MigracionAuditoria.RolApp, Migracion.ClaveApp);
    public string NodoBus => $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";
    private Uri UriAdministracion => new($"http://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(15672)}/api/");

    public Productor this[string productor] => _productores[productor];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await Migracion.MigrarAsync(Dueno);
        foreach (string nombre in Productores)
        {
            string cadena = Migracion.Base(Servidor, "productor_" + nombre.Replace("-", "_"));
            Productor productor = new(nombre, cadena, OpcionesBus(nombre));
            await using (ContextoProductor db = productor.Contexto())
            {
                await db.Database.EnsureCreatedAsync();
            }
            _productores[nombre] = productor;
        }
    }

    public async Task DisposeAsync()
    {
        foreach (Productor p in _productores.Values)
        {
            await p.DisposeAsync();
        }
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>Opciones del bus de un servicio. Las claves de desarrollo son "&lt;usuario&gt;_local" (generar_definiciones.py).</summary>
    public OpcionesBus OpcionesBus(string usuario, string? cola = null) => new()
    {
        Nodos = [NodoBus],
        Usuario = usuario,
        Clave = usuario.Replace("-", "_") + "_local",
        Productor = usuario,
        Cola = cola,
        IntervaloRelevoMs = 100,
        TamanoLote = 50,
    };

    public AuditoriaDbContext ComoApp() => new(new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql(App).Options);

    /// <summary>El servicio de Auditoría completo (consumidor, procesos y API), configurado como en el despliegue.</summary>
    public WebApplicationFactory<Program> Servicio(int plazoHuecoSegundos = 60, int intervaloRevisionMs = 500) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Auditoria", App);
            b.UseSetting("Bus:Nodos:0", NodoBus);
            b.UseSetting("Bus:Usuario", "auditoria");
            b.UseSetting("Bus:Clave", "auditoria_local");
            b.UseSetting("Bus:Cola", "q.auditoria");
            b.UseSetting("Auditoria:PlazoHuecoSegundos", plazoHuecoSegundos.ToString());
            b.UseSetting("Auditoria:IntervaloRevisionMs", intervaloRevisionMs.ToString());
            b.ConfigureLogging(l => l.ClearProviders());
        });

    public static HttpClient Cliente(WebApplicationFactory<Program> servicio, string usuario = "contraloria", string roles = "auditor")
    {
        HttpClient cliente = servicio.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Usuario", usuario);
        cliente.DefaultRequestHeaders.Add("X-Roles", roles);
        return cliente;
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
        string json = await cliente.GetStringAsync($"queues/%2F/{Uri.EscapeDataString(cola)}");
        using JsonDocument documento = JsonDocument.Parse(json);
        return documento.RootElement.TryGetProperty("messages", out JsonElement m) ? m.GetInt64() : 0;
    }

    /// <summary>Publica un mensaje tal cual en el exchange de la plataforma, como lo haría un productor cualquiera.</summary>
    public async Task PublicarCrudoAsync(string claveRuteo, string cuerpo)
    {
        using HttpClient cliente = Administracion();
        string payload = JsonSerializer.Serialize(new
        {
            properties = new { message_id = Guid.NewGuid().ToString(), delivery_mode = 2 },
            routing_key = claveRuteo,
            payload = cuerpo,
            payload_encoding = "string",
        });
        HttpResponseMessage respuesta = await cliente.PostAsync("exchanges/%2F/plataforma.eventos/publish",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        respuesta.EnsureSuccessStatusCode();
        string resultado = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("\"routed\":true", resultado);
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
            await Task.Delay(100);
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
public sealed class ColeccionAuditoria : ICollectionFixture<EntornoAuditoria>
{
    public const string Nombre = "auditoria";
}
