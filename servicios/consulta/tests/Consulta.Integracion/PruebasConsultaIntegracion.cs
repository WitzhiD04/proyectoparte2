using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Consulta.Infraestructura;
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
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Consulta.Integracion;

public sealed class EntornoConsulta : IAsyncLifetime
{
    public const string Clave = "consulta_prueba";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:18-3.6").WithDatabase("consulta").Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:8.8.4-alpine").Build();
    private readonly IContainer _rabbit;
    private WebApplicationFactory<Program>? _fabrica;

    public EntornoConsulta()
    {
        string bus = Path.Combine(Raiz(), "despliegue", "bus");
        _rabbit = new ContainerBuilder("rabbitmq:4.4.0-management").WithPortBinding(5672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "definiciones.json")), "/etc/rabbitmq/definiciones.json")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "rabbitmq-un-nodo.conf")), "/etc/rabbitmq/rabbitmq.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete")).Build();
    }

    public string App => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "consulta_app", Password = Clave }.ConnectionString;
    public HttpClient Cliente { get; private set; } = null!;
    public IServiceProvider Servicios => _fabrica!.Services;

    /// <summary>Un cliente sin los encabezados de identidad.</summary>
    public HttpClient NuevoCliente() => _fabrica!.CreateClient();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), Redis.StartAsync(), _rabbit.StartAsync());
        await MigracionConsulta.EjecutarAsync(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ConsultaDueno"] = _postgres.GetConnectionString(),
            ["Migracion:ClaveConsultaApp"] = Clave,
        }).Build(), NullLogger.Instance);
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Consulta", App);
            b.UseSetting("Redis:Conexion", Redis.GetConnectionString());
            b.UseSetting("Bus:Nodos:0", $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}");
            b.UseSetting("Bus:Clave", "consulta_local");
        });
        Cliente = _fabrica.CreateClient();
        Cliente.DefaultRequestHeaders.Add("X-Usuario", "auditor.1");
        Cliente.DefaultRequestHeaders.Add("X-Roles", "auditor");
    }

    public async Task DisposeAsync()
    {
        if (_fabrica is not null) await _fabrica.DisposeAsync();
        await _postgres.DisposeAsync();
        await Redis.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>Entrega un sobre como lo haría el consumidor del bus, en su transacción idempotente.</summary>
    public async Task EntregarAsync(Sobre sobre)
    {
        using IServiceScope ambito = Servicios.CreateScope();
        ConsultaDbContext db = ambito.ServiceProvider.GetRequiredService<ConsultaDbContext>();
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await ambito.ServiceProvider.GetRequiredService<IManejadorEvento>().ManejarAsync(sobre with { SecuenciaProductor = 1 }, default);
        await db.SaveChangesAsync();
        await transaccion.CommitAsync();
    }

    private static string Raiz()
    {
        DirectoryInfo? c = new(AppContext.BaseDirectory);
        while (c is not null && !File.Exists(Path.Combine(c.FullName, "Plataforma.sln"))) c = c.Parent;
        return c?.FullName ?? throw new DirectoryNotFoundException("No se encontró Plataforma.sln.");
    }
}

public class PruebasConsultaIntegracion : IClassFixture<EntornoConsulta>
{
    private readonly EntornoConsulta _e;
    public PruebasConsultaIntegracion(EntornoConsulta e) { _e = e; }

    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private static JsonElement Poligono(double lon, double lat, double d = 0.01) => JsonSerializer.SerializeToElement(new
    {
        type = "Polygon",
        coordinates = new[] { new[] { new[] { lon, lat }, new[] { lon + d, lat }, new[] { lon + d, lat + d }, new[] { lon, lat + d }, new[] { lon, lat } } },
    });

    private static Sobre S<T>(DefinicionEvento def, Guid id, string sufijo, T datos, DateTimeOffset t) =>
        Sobre.Crear(def, CatalogoEventos.GestionDesastres, $"desastre:{id}:{sufijo}", datos, t);

    private async Task<Guid> RegistrarAsync(bool desordenado = false)
    {
        Guid id = Guid.NewGuid();
        Sobre creado = S(CatalogoEventos.DesastreCreado, id, "v1", new DesastreCreadoV1(id, "TERREMOTO", Severidad.NIVEL_2, 1, EstadoDesastre.REGISTRADO,
            "ungrd", "ungrd", OrigenDesastre.MANUAL, "Sismo de prueba", "d", T0, new Ubicacion(4.53, -75.68), 1), T0);
        Sobre zona = S(CatalogoEventos.ZonaActualizada, id, "v1:zona", new ZonaActualizadaV1(id, 1, Poligono(-75.69, 4.53), ["63001"], "DecisionAutoridad", "a"), T0);
        Sobre severidad = S(CatalogoEventos.SeveridadCambiada, id, "v2", new SeveridadCambiadaV1(id, Severidad.NIVEL_2, Severidad.NIVEL_3, 2, "a", "réplicas"),
            T0.AddHours(2));
        // Con dos réplicas consumiendo la cola, un cambio puede aplicarse antes que el registro.
        foreach (Sobre s in desordenado ? new[] { severidad, zona, creado } : new[] { creado, zona, severidad })
        {
            await _e.EntregarAsync(s);
        }
        return id;
    }

    [Fact]
    public async Task Los_eventos_desordenados_dejan_la_proyeccion_correcta_con_la_marca_de_cada_dato()
    {
        Guid id = await RegistrarAsync(desordenado: true);
        JsonElement vista = await _e.Cliente.GetFromJsonAsync<JsonElement>($"/v1/consulta/desastres/{id}");
        Assert.Equal("NIVEL_3", vista.GetProperty("severidad").GetProperty("valor").GetString());
        Assert.Equal(T0.AddHours(2), vista.GetProperty("severidad").GetProperty("datoAl").GetDateTimeOffset());
        Assert.Equal("REGISTRADO", vista.GetProperty("estado").GetProperty("valor").GetString());
        Assert.Equal("Sismo de prueba", vista.GetProperty("nombre").GetString());
        Assert.Equal(T0.AddHours(2), vista.GetProperty("datoAl").GetDateTimeOffset());
    }

    [Fact]
    public async Task El_mapa_trae_la_zona_simplificada_y_un_cambio_de_zona_lo_invalida()
    {
        Guid id = await RegistrarAsync();
        JsonElement mapa = await _e.Cliente.GetFromJsonAsync<JsonElement>("/v1/consulta/mapa");
        JsonElement feature = mapa.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("properties").GetProperty("idDesastre").GetGuid() == id);
        Assert.Equal("Polygon", feature.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(1, feature.GetProperty("properties").GetProperty("zonaVersion").GetInt32());
        Assert.False(mapa.GetProperty("degradado").GetBoolean());

        await _e.EntregarAsync(S(CatalogoEventos.ZonaActualizada, id, "v3", new ZonaActualizadaV1(id, 2, Poligono(-75.69, 4.53, 0.05), ["63001", "63130"],
            "EvaluacionCampo", "a"), T0.AddHours(4)));
        JsonElement despues = await _e.Cliente.GetFromJsonAsync<JsonElement>("/v1/consulta/mapa");
        Assert.Equal(2, despues.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("properties").GetProperty("idDesastre").GetGuid() == id).GetProperty("properties").GetProperty("zonaVersion").GetInt32());
    }

    [Fact]
    public async Task La_evolucion_reconstruye_el_desastre_en_un_instante_pasado()
    {
        Guid id = await RegistrarAsync();
        await _e.EntregarAsync(S(CatalogoEventos.ZonaActualizada, id, "v3", new ZonaActualizadaV1(id, 2, Poligono(-75.69, 4.53, 0.05), ["63001", "63130"],
            "EvaluacionCampo", "a"), T0.AddHours(4)));
        JsonElement evolucion = await _e.Cliente.GetFromJsonAsync<JsonElement>(
            $"/v1/consulta/desastres/{id}/evolucion?en={Uri.EscapeDataString(T0.AddHours(3).ToString("O"))}");
        Assert.Equal(4, evolucion.GetProperty("cambios").GetArrayLength());
        JsonElement enInstante = evolucion.GetProperty("enInstante");
        Assert.Equal("NIVEL_3", enInstante.GetProperty("severidad").GetString());
        Assert.Equal(1, enInstante.GetProperty("zona").GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("/v1/consulta/desastres")]
    [InlineData("/v1/consulta/mapa")]
    [InlineData("/v1/integraciones")]
    public async Task Sin_identidad_la_consulta_responde_401(string ruta)
    {
        using HttpClient sinIdentidad = _e.NuevoCliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await sinIdentidad.GetAsync(ruta)).StatusCode);
    }

    [Theory]
    [InlineData("DELETE FROM p_desastres")]
    [InlineData("DROP TABLE p_zonas")]
    public async Task El_servicio_no_puede_borrar_ni_cambiar_el_esquema(string sql)
    {
        await using NpgsqlConnection c = new(_e.App);
        await c.OpenAsync();
        await using NpgsqlCommand comando = new(sql, c);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
