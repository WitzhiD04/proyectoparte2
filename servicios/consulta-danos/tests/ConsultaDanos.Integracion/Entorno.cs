using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ConsultaDanos.Infraestructura;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ConsultaDanos.Integracion;

/// <summary>
/// PostGIS 18, Redis 8.8 y RabbitMQ 4.4 con la topología real de despliegue/bus/definiciones.json (un nodo). El servicio
/// corre con WebApplicationFactory y consume de q.consulta-danos con su propio usuario del bus.
/// </summary>
public sealed class EntornoConsultaDanos : IAsyncLifetime
{
    public const string Clave = "consulta_danos_prueba";
    /// <summary>Un puerto donde no escucha nadie: la conexión se rechaza en el acto (simula la caída de un componente).</summary>
    public const string Caido = "127.0.0.1:1";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:18-3.6").WithDatabase("consulta_danos").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.8.4-alpine").Build();
    private readonly IContainer _rabbit;
    private WebApplicationFactory<Program>? _fabrica;

    public EntornoConsultaDanos()
    {
        string bus = Path.Combine(Raiz(), "despliegue", "bus");
        _rabbit = new ContainerBuilder("rabbitmq:4.4.0-management").WithPortBinding(5672, true).WithPortBinding(15672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "definiciones.json")), "/etc/rabbitmq/definiciones.json")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "rabbitmq-un-nodo.conf")), "/etc/rabbitmq/rabbitmq.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete")).Build();
    }

    public string App => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    { Username = MigracionConsultaDanos.RolAplicacion, Password = Clave }.ConnectionString;

    public string Redis => _redis.GetConnectionString();
    public string NodoBus => $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";
    public HttpClient Cliente { get; private set; } = null!;
    public IServiceProvider Servicios => _fabrica!.Services;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());
        await MigracionConsultaDanos.EjecutarAsync(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ConsultaDanosDueno"] = _postgres.GetConnectionString(),
            ["Migracion:ClaveConsultaDanosApp"] = Clave,
        }).Build(), NullLogger.Instance);
        _fabrica = Fabrica(App, Redis, NodoBus);
        Cliente = ConIdentidad(_fabrica.CreateClient());

        // La capa de municipios (EV-21), como la publica el adaptador de SE-2.
        await EntregarAsync(Sobre.Crear(CatalogoEventos.CapaGeograficaActualizada, CatalogoEventos.AdaptadorSe2, "capa:MUNICIPIOS:v1:1",
            new CapaGeograficaActualizadaV1(CapaGeografica.MUNICIPIOS, 1,
            [
                new Division("63001", "Armenia", "63", Poligono(-75.75, 4.50, 0.1)),
                new Division("63130", "Calarcá", "63", Poligono(-75.65, 4.50, 0.1)),
                new Division("76001", "Cali", "76", Poligono(-76.50, 3.40, 0.1)),
                new Division("76109", "Buenaventura", "76", Poligono(-77.00, 3.90, 0.1)),
            ], "SE-2", 1, 1), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    public async Task DisposeAsync()
    {
        if (_fabrica is not null) await _fabrica.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>Una instancia del servicio con la base, la caché y el bus que se le indiquen (para simular caídas).</summary>
    public WebApplicationFactory<Program> Fabrica(string baseDatos, string redis, string bus) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:ConsultaDanos", baseDatos);
            b.UseSetting("Redis:Conexion", redis);
            b.UseSetting("Bus:Nodos:0", bus);
            b.UseSetting("Bus:Clave", "consulta_danos_local");
        });

    public static HttpClient ConIdentidad(HttpClient cliente)
    {
        cliente.DefaultRequestHeaders.Add("X-Usuario", "ungrd.operador");
        cliente.DefaultRequestHeaders.Add("X-Roles", "autoridad");
        return cliente;
    }

    /// <summary>Un cliente sin los encabezados de identidad.</summary>
    public HttpClient SinIdentidad() => _fabrica!.CreateClient();

    /// <summary>Entrega un sobre como lo haría el consumidor del bus, en su transacción idempotente.</summary>
    public async Task EntregarAsync(Sobre sobre)
    {
        using IServiceScope ambito = Servicios.CreateScope();
        ConsultaDanosDbContext db = ambito.ServiceProvider.GetRequiredService<ConsultaDanosDbContext>();
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await ambito.ServiceProvider.GetRequiredService<IManejadorEvento>().ManejarAsync(sobre with { SecuenciaProductor = 1 }, default);
        await db.SaveChangesAsync();
        await transaccion.CommitAsync();
    }

    /// <summary>Publica un sobre en el exchange de la plataforma, como lo haría el relevo del productor.</summary>
    public async Task PublicarAsync(Sobre sobre)
    {
        using HttpClient cliente = new() { BaseAddress = new Uri($"http://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(15672)}/api/") };
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin_local")));
        string cuerpo = JsonSerializer.Serialize(new
        {
            properties = new { message_id = sobre.IdEvento.ToString(), delivery_mode = 2 },
            routing_key = CatalogoEventos.PorTipo(sobre.Tipo).ClaveRuteo,
            payload = (sobre with { SecuenciaProductor = 1 }).Serializar(),
            payload_encoding = "string",
        });
        HttpResponseMessage r = await cliente.PostAsync("exchanges/%2F/plataforma.eventos/publish",
            new StringContent(cuerpo, Encoding.UTF8, "application/json"));
        r.EnsureSuccessStatusCode();
        Assert.Contains("\"routed\":true", await r.Content.ReadAsStringAsync());
    }

    public async Task<bool> ExisteEnCacheAsync(string patron)
    {
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(Redis);
        IServer servidor = redis.GetServer(redis.GetEndPoints()[0]);
        await foreach (RedisKey _ in servidor.KeysAsync(pattern: patron)) return true;
        return false;
    }

    /// <summary>Sube la versión del desastre en la caché, como si la respuesta armada hubiera expirado.</summary>
    public async Task ExpirarRespuestasAsync(Guid desastre)
    {
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(Redis);
        await redis.GetDatabase().StringIncrementAsync($"consulta-danos:version:{desastre}");
    }

    public static JsonElement Poligono(double lon, double lat, double d) => JsonSerializer.SerializeToElement(new
    {
        type = "Polygon",
        coordinates = new[] { new[] { new[] { lon, lat }, new[] { lon + d, lat }, new[] { lon + d, lat + d }, new[] { lon, lat + d }, new[] { lon, lat } } },
    });

    private static string Raiz()
    {
        DirectoryInfo? c = new(AppContext.BaseDirectory);
        while (c is not null && !File.Exists(Path.Combine(c.FullName, "Plataforma.sln"))) c = c.Parent;
        return c?.FullName ?? throw new DirectoryNotFoundException("No se encontró Plataforma.sln.");
    }
}

/// <summary>Los eventos de Evaluación de Daños con el sobre y las claves de idempotencia que usa su productor.</summary>
public static class Eventos
{
    public static Sobre Sincronizada(Guid activo, Guid evaluacion, Guid desastre, NivelDano nivel, int versionEvaluacion, int versionActivo,
        DateTimeOffset t, string municipio = "63001", string tipo = "VIVIENDA", bool enConflicto = false, double lon = -75.70) =>
        Sobre.Crear(CatalogoEventos.EvaluacionSincronizada, CatalogoEventos.EvaluacionDanos,
            $"evaluacion:{evaluacion}:v{versionEvaluacion}:sincronizada",
            new EvaluacionSincronizadaV1(evaluacion, "dispositivo-1", t, t, enConflicto, versionEvaluacion, activo, desastre, tipo, nivel,
                new Ubicacion(4.53, lon), 5, municipio, enConflicto ? EstadoEvaluacion.EN_CONFLICTO : EstadoEvaluacion.REGISTRADA,
                versionEvaluacion, versionActivo), t);

    public static Sobre Validada(Guid activo, Guid evaluacion, Guid desastre, NivelDano nivel, int version, EstadoValidacion estado,
        DateTimeOffset t, string municipio = "63001", string tipo = "VIVIENDA") =>
        Sobre.Crear(CatalogoEventos.EvaluacionValidada, CatalogoEventos.EvaluacionDanos, $"evaluacion:{evaluacion}:v{version}:validacion",
            new EvaluacionValidadaV1(evaluacion, desastre, tipo, nivel, new Ubicacion(4.53, -75.70), "evaluador.armenia", "alcaldia-armenia",
                version, activo, "Casa de prueba", municipio, municipio[..2], estado, "ungrd.operador", t), t);

    public static Sobre ConflictoDetectado(Guid conflicto, Guid activo, Guid vigente, Guid entrante, DateTimeOffset t) =>
        Sobre.Crear(CatalogoEventos.ConflictoDetectado, CatalogoEventos.EvaluacionDanos, $"conflicto:{conflicto}:detectado",
            new ConflictoDetectadoV1(conflicto, [vigente, entrante], "regla de prueba",
                [new VersionConservada(vigente, 1, true), new VersionConservada(entrante, 2, false)], EstadoRevision.PENDIENTE, activo,
                TipoConflicto.VERSION), t);

    public static Sobre ConflictoResuelto(Guid conflicto, Guid activo, Guid evaluacion, Guid desastre, NivelDano nivel, int versionEvaluacion,
        int versionActivo, DateTimeOffset t) =>
        Sobre.Crear(CatalogoEventos.ConflictoResuelto, CatalogoEventos.EvaluacionDanos, $"conflicto:{conflicto}:resuelto",
            new ConflictoResueltoV1(conflicto, DecisionConflicto.PREVALECE_VIGENTE, evaluacion, versionEvaluacion, "operador.danos",
                "Se conserva la vigente", new InstantaneaEvaluacion(evaluacion, activo, desastre, "VIVIENDA", nivel, new Ubicacion(4.53, -75.70),
                    5, "63001", EstadoEvaluacion.REGISTRADA, versionEvaluacion, versionActivo)), t);
}
