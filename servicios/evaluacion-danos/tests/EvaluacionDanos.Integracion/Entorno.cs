using System.Net.Http.Json;
using AdaptadorSe2;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EvaluacionDanos.Api.Controllers;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Dominio;
using EvaluacionDanos.Infraestructura;
using EvaluacionDanos.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Testcontainers.PostgreSql;
using EstadoDesastreCanonico = Plataforma.Comun.Eventos.EstadoDesastre;

namespace EvaluacionDanos.Integracion;

[CollectionDefinition(Nombre)]
public class ColeccionApi : ICollectionFixture<Entorno>
{
    public const string Nombre = "Evaluación de Daños: dos réplicas sobre PostGIS y MinIO";
}

/// <summary>Una réplica del servicio en memoria. El relevo y el consumidor del bus no arrancan: aquí no hay bus.</summary>
public sealed class Replica(string cadenaApp, string minio) : WebApplicationFactory<SincronizacionController>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuracion) => configuracion.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:EvaluacionDanos"] = cadenaApp,
            ["Minio:Endpoint"] = minio,
            ["Minio:Usuario"] = Entorno.UsuarioMinio,
            ["Minio:Clave"] = Entorno.ClaveMinio,
        }));
        builder.ConfigureTestServices(servicios =>
        {
            foreach (ServiceDescriptor servicio in servicios.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType is { IsGenericType: true } tipo
                && (tipo.GetGenericTypeDefinition() == typeof(RelevoBandeja<>)
                    || tipo.GetGenericTypeDefinition() == typeof(ConsumidorIdempotente<>))).ToList())
            {
                servicios.Remove(servicio);
            }
        });
    }
}

/// <summary>
/// El despliegue real en pequeño: PostgreSQL 18 con PostGIS migrado por el migrador (con el catálogo sembrado), MinIO,
/// y DOS réplicas del servicio conectadas con el rol de la aplicación a la misma base y al mismo almacén (AD-24).
/// La capa de municipios y los desastres entran como en producción: sobres EV-21 (generados por el código del
/// adaptador de SE-2 con el archivo real), EV-03, EV-05, EV-06 y EV-27 aplicados por el consumidor idempotente.
/// </summary>
public sealed class Entorno : IAsyncLifetime
{
    public const string ClaveApp = "evaluacion_danos_prueba";
    public const string UsuarioMinio = "minioadmin";
    public const string ClaveMinio = "minioadmin";
    public const string Bucket = "evaluacion-danos-evidencias";

    // Los mismos identificadores que sembraba el prototipo, para las pruebas portadas.
    public static readonly Guid DesastreInundacion = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    public static readonly Guid DesastreSismo = Guid.Parse("d0000000-0000-0000-0000-000000000002");
    public static readonly Guid DesastreDeslizamiento = Guid.Parse("d0000000-0000-0000-0000-000000000003");
    public static readonly Guid DesastreCerrado = Guid.Parse("d0000000-0000-0000-0000-000000000004");
    public const string OrganismoParticipante = "defensa-civil";
    public static readonly DateTimeOffset Instante = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private static int siguienteActivo;
    private static int siguienteZona;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:18-3.6").Build();
    private readonly IContainer _minio = new ContainerBuilder("pgsty/minio")
        .WithCommand("server", "/data")
        .WithEnvironment("MINIO_ROOT_USER", UsuarioMinio)
        .WithEnvironment("MINIO_ROOT_PASSWORD", ClaveMinio)
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/minio/health/live")))
        .Build();

    private Replica[] _replicas = [];
    private IMinioClient _clienteMinio = null!;

    public string Dueno => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "evaluacion_danos" }.ConnectionString;
    public string CadenaApp => new NpgsqlConnectionStringBuilder(Dueno) { Username = MigracionEvaluacionDanos.RolApp, Password = ClaveApp }.ConnectionString;
    public IConfiguration ConfiguracionMigrador => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:EvaluacionDanosDueno"] = Dueno,
        ["Migracion:ClaveEvaluacionDanosApp"] = ClaveApp,
    }).Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
        await MigracionEvaluacionDanos.EjecutarAsync(ConfiguracionMigrador, sembrar: true, NullLogger.Instance);

        string minio = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}";
        _clienteMinio = new MinioClient().WithEndpoint(minio).WithCredentials(UsuarioMinio, ClaveMinio).WithSSL(false).Build();
        _replicas = [new Replica(CadenaApp, minio), new Replica(CadenaApp, minio)];
        _ = _replicas[0].Server;
        _ = _replicas[1].Server;

        // Capa de municipios: las 33 partes de EV-21, tal como las arma el adaptador con el archivo real.
        foreach (Sobre sobre in SobresDeLaCapa())
        {
            Assert.True(await AplicarEventoAsync(sobre));
        }
        // Desastres: tres activos (los del prototipo) y uno cerrado, que la descarga no debe entregar (DA-19).
        await AplicarEventoAsync(DesastreCreado(DesastreInundacion, "Inundación río Bogotá", "INUNDACION"));
        await AplicarEventoAsync(DesastreCreado(DesastreSismo, "Sismo Sabana de Bogotá", "SISMO"));
        await AplicarEventoAsync(DesastreCreado(DesastreDeslizamiento, "Deslizamiento en Usme", "DESLIZAMIENTO"));
        await AplicarEventoAsync(DesastreCreado(DesastreCerrado, "Vendaval de 2025", "VENDAVAL"));
        await AplicarEventoAsync(Evento(CatalogoEventos.EstadoDesastreCambiado, $"desastre:{DesastreCerrado}:v2",
            new EstadoDesastreCambiadoV1(DesastreCerrado, EstadoDesastreCanonico.EN_EVALUACION, EstadoDesastreCanonico.CERRADO, "coordinador", 2)));
        await AplicarEventoAsync(Organismos(DesastreInundacion, 1, "ungrd", OrganismoParticipante, "alcaldia.armenia"));
        await AplicarEventoAsync(Evento(CatalogoEventos.ZonaActualizada, $"desastre:{DesastreSismo}:zona:v1",
            new ZonaActualizadaV1(DesastreSismo, 1, null, ["63001", "63130"], "SE-2", "coordinador")));

        await using var db = NuevoContexto();
        await ActivosDePrueba.SembrarAsync(db);   // activos solo para las pruebas
    }

    public async Task DisposeAsync()
    {
        foreach (Replica replica in _replicas)
        {
            await replica.DisposeAsync();
        }
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
    }

    // ---------- Clientes ----------

    /// <summary>Cliente sin identidad, de la réplica indicada.</summary>
    public HttpClient CreateClient(int replica = 0) => _replicas[replica].CreateClient();

    /// <summary>Como la app actual: usuario, organización y dispositivo, sin roles (se trata como evaluador).</summary>
    public HttpClient Cliente(string dispositivo, string usuario = "Evaluador de prueba", int replica = 0) =>
        ConEncabezados(new Dictionary<string, string>
        {
            ["X-Usuario"] = usuario, ["X-Organizacion"] = "Organización de prueba", ["X-Dispositivo"] = dispositivo,
        }, replica);

    public HttpClient ConRol(string rol, string usuario, string? organizacion = null, int replica = 0)
    {
        var encabezados = new Dictionary<string, string> { ["X-Usuario"] = usuario, ["X-Roles"] = rol };
        if (organizacion != null)
        {
            encabezados["X-Organizacion"] = organizacion;
        }
        return ConEncabezados(encabezados, replica);
    }

    public HttpClient Operador(int replica = 0) => ConRol("operador", "operador.1", replica: replica);

    public HttpClient ConEncabezados(IReadOnlyDictionary<string, string> encabezados, int replica = 0)
    {
        var cliente = _replicas[replica].CreateClient();
        foreach (var (nombre, valor) in encabezados)
        {
            cliente.DefaultRequestHeaders.Add(nombre, valor);
        }
        return cliente;
    }

    /// <summary>Un cliente que reparte las peticiones entre las dos réplicas, una y una, como un balanceador.</summary>
    public HttpClient ClienteAlternado(IReadOnlyDictionary<string, string> encabezados)
    {
        var cliente = new HttpClient(new Alternador(_replicas.Select(r => r.Server.CreateHandler()).ToArray()))
        {
            BaseAddress = new Uri("http://localhost"),
        };
        foreach (var (nombre, valor) in encabezados)
        {
            cliente.DefaultRequestHeaders.Add(nombre, valor);
        }
        return cliente;
    }

    private sealed class Alternador(HttpMessageHandler[] replicas) : HttpMessageHandler
    {
        private static int turno;
        private readonly HttpMessageInvoker[] _invocadores = replicas.Select(r => new HttpMessageInvoker(r)).ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct) =>
            _invocadores[(uint)Interlocked.Increment(ref turno) % _invocadores.Length].SendAsync(peticion, ct);
    }

    // ---------- Base, bus y almacén ----------

    /// <summary>Contexto con el rol de la aplicación, el mismo con que corre el servicio.</summary>
    public EvaluacionDanosDbContext NuevoContexto() => new(MigracionEvaluacionDanos.Opciones(CadenaApp));

    /// <summary>
    /// Aplica un sobre como lo haría el consumidor de q.evaluacion-danos: valida el contrato y, en una transacción,
    /// aplica el efecto y lo registra como procesado. Devuelve falso si ya estaba procesado.
    /// </summary>
    public async Task<bool> AplicarEventoAsync(Sobre sobre, int replica = 0)
    {
        ValidadorContrato.Instancia.Validar(sobre);
        IServiceProvider servicios = _replicas[replica].Services;
        var consumidor = new ConsumidorIdempotente<EvaluacionDanosDbContext>(
            servicios.GetRequiredService<IServiceScopeFactory>(), servicios.GetRequiredService<ConexionBus>(),
            servicios.GetRequiredService<OpcionesBus>(), NullLogger<ConsumidorIdempotente<EvaluacionDanosDbContext>>.Instance);
        return await consumidor.AplicarAsync(sobre, CancellationToken.None);
    }

    public static IReadOnlyList<Sobre> SobresDeLaCapa() =>
        Capas.ASobres(Capas.Leer(File.ReadAllBytes(Path.Combine(RaizRepositorio(), "datos", "geo", "municipios-colombia.geojson"))), Instante);

    public static Sobre Evento<TDatos>(DefinicionEvento definicion, string clave, TDatos datos) =>
        Sobre.Crear(definicion, CatalogoEventos.GestionDesastres, clave, datos, Instante, idCorrelacion: "prueba");

    public static Sobre DesastreCreado(Guid id, string nombre, string tipo, int version = 1,
        EstadoDesastreCanonico estado = EstadoDesastreCanonico.EN_EVALUACION) =>
        Evento(CatalogoEventos.DesastreCreado, $"desastre:{id}:v{version}", new DesastreCreadoV1(id, tipo, Severidad.NIVEL_2, 1,
            estado, "ungrd", "coordinador", OrigenDesastre.MANUAL, nombre, nombre, Instante.AddDays(-2),
            new Ubicacion(4.6, -74.1), version));

    public static Sobre Organismos(Guid id, int version, string responsable, params string[] participantes) =>
        Evento(CatalogoEventos.OrganismosDesastreCambiados, $"desastre:{id}:organismos:v{version}",
            new OrganismosDesastreCambiadosV1(id, responsable,
                participantes.Select(p => new Participante(p, "Evaluación de daños")).ToList(), version, "coordinador"));

    /// <summary>El objeto del almacén con esa clave (contenido y fecha de modificación), o null si no existe.</summary>
    public async Task<(byte[] Contenido, DateTime Modificado)?> ObjetoAsync(string clave)
    {
        try
        {
            var estado = await _clienteMinio.StatObjectAsync(new StatObjectArgs().WithBucket(Bucket).WithObject(clave));
            using var memoria = new MemoryStream();
            await _clienteMinio.GetObjectAsync(new GetObjectArgs().WithBucket(Bucket).WithObject(clave)
                .WithCallbackStream(flujo => flujo.CopyTo(memoria)));
            return (memoria.ToArray(), estado.LastModified);
        }
        catch (Exception ex) when (ex is ObjectNotFoundException or BucketNotFoundException)
        {
            return null;
        }
    }

    // ---------- Datos de las pruebas ----------

    /// <summary>Cada prueba toma activos de prueba distintos para no pisarse con las demás.</summary>
    public static Guid TomarActivoSembrado()
    {
        int n = Interlocked.Increment(ref siguienteActivo);
        if (n > ActivosDePrueba.Total)
        {
            throw new InvalidOperationException("Las pruebas usaron más activos que los sembrados");
        }
        return ActivosDePrueba.Id(n);
    }

    /// <summary>Una zona vacía (lejos de Bogotá y de las demás pruebas) para crear activos nuevos.</summary>
    public static (double Lat, double Lon) TomarZonaLibre()
    {
        int n = Interlocked.Increment(ref siguienteZona);
        return (10 + n * 0.05, -70 - n * 0.05);
    }

    public static async Task<RespuestaSincronizacionDto> Sincronizar(HttpClient cliente, LoteSincronizacion lote)
    {
        var respuesta = await cliente.PostAsJsonAsync("/v1/sincronizacion/evaluaciones", lote, JsonEvaluacion.Opciones);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.IsSuccessStatusCode, $"HTTP {(int)respuesta.StatusCode}: {texto}");
        return JsonEvaluacion.Deserializar<RespuestaSincronizacionDto>(texto);
    }

    public static string RaizRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Plataforma.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró Plataforma.sln subiendo desde " + AppContext.BaseDirectory);
    }
}
