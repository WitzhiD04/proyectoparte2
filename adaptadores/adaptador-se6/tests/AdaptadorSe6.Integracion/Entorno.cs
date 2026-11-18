using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using RabbitMQ.Client;
using SimSe6;
using Testcontainers.PostgreSql;

namespace AdaptadorSe6.Integracion;

/// <summary>El simulador de SE-6 en memoria para las pruebas de integración.</summary>
public sealed class SimuladorSe6 : WebApplicationFactory<MarcadorSimSe6>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // En modo lento responde después de 30 s: mucho más que el tiempo de espera del adaptador en las pruebas.
        builder.UseSetting("Simulador:RetrasoLentoMs", "30000");
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
    }

    public RegistroSe6 Registro => Services.GetRequiredService<RegistroSe6>();
}

/// <summary>
/// PostgreSQL 18 con la base "adaptadores" migrada por el migrador del adaptador (DT-19) y RabbitMQ 4.4 con la topología
/// real de despliegue/bus/definiciones.json (un nodo), como en adaptador-se7. Se levantan una vez para toda la colección.
/// </summary>
public sealed class EntornoSe6 : IAsyncLifetime
{
    public const string ClaveApp = "adaptador_se6_prueba";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4").Build();
    private readonly IContainer _rabbit;

    public EntornoSe6()
    {
        string bus = Path.Combine(RaizRepositorio(), "despliegue", "bus");
        _rabbit = new ContainerBuilder("rabbitmq:4.4.0-management")
            .WithPortBinding(5672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "definiciones.json")), "/etc/rabbitmq/definiciones.json")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "rabbitmq-un-nodo.conf")), "/etc/rabbitmq/rabbitmq.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
    }

    public string Dueno => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = MigracionAdaptadorSe6.Base }.ConnectionString;
    public string App => new NpgsqlConnectionStringBuilder(Dueno) { Username = MigracionAdaptadorSe6.RolApp, Password = ClaveApp }.ConnectionString;
    public string NodoBus => $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";

    public IConfiguration ConfiguracionMigrador => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:AdaptadorSe6Dueno"] = Dueno,
        ["Migracion:ClaveAdaptadorSe6App"] = ClaveApp,
    }).Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await MigracionAdaptadorSe6.EjecutarAsync(ConfiguracionMigrador, NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public AdaptadorSe6DbContext ComoApp() => new(MigracionAdaptadorSe6.Opciones(App));

    /// <summary>
    /// Un proceso del adaptador con el mismo cableado que Program.cs, tiempos cortos y SE-6 en memoria.
    /// </summary>
    public IHost Adaptador(SimuladorSe6 se6, Dictionary<string, string?>? extra = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        Dictionary<string, string?> valores = RegistroAdaptadorSe6.ValoresPorOmision();
        valores["ConnectionStrings:AdaptadorSe6"] = App;
        valores["Bus:Nodos:0"] = NodoBus;
        valores["Bus:Clave"] = "adaptador_se6_local";
        valores["Bus:IntervaloRelevoMs"] = "100";
        valores["Se6:Url"] = "http://localhost/";
        valores["Se6:TamanoLote"] = "4";
        valores["Se6:VentanaLoteSegundos"] = "0.25";
        valores["Resiliencia:TiempoEsperaIntentoSegundos"] = "0.3";
        valores["Resiliencia:TiempoEsperaTotalSegundos"] = "5";
        valores["Resiliencia:EsperaBaseReintentoSegundos"] = "0.05";
        valores["Resiliencia:DuracionAperturaSegundos"] = "1";
        valores["Salida:EsperaReintentoSegundos"] = "0.2";
        valores["Salida:IntervaloSondeoSegundos"] = "0.5";
        foreach ((string clave, string? valor) in extra ?? [])
        {
            valores[clave] = valor;
        }
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(valores);
        builder.Logging.ClearProviders();
        builder.Services.AgregarModoSalida(builder.Configuration);

        builder.Services.AddHttpClient<ClienteSe6>()
            .ConfigurePrimaryHttpMessageHandler(() => new ClienteQueAbandona { InnerHandler = se6.Server.CreateHandler() });
        return builder.Build();
    }

    private sealed class ClienteQueAbandona : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            Task<HttpResponseMessage> envio = base.SendAsync(peticion, ct);
            await Task.WhenAny(envio, Task.Delay(Timeout.Infinite, ct));
            ct.ThrowIfCancellationRequested();
            return await envio;
        }
    }

    // ----------------------------------------------------------------- bus

    private async Task<IConnection> ConectarAsync(string usuario)
    {
        string[] partes = NodoBus.Split(':');
        ConnectionFactory fabrica = new()
        {
            HostName = partes[0], Port = int.Parse(partes[1]), UserName = usuario,
            Password = usuario == "admin" ? "admin_local" : usuario.Replace("-", "_") + "_local",
        };
        return await fabrica.CreateConnectionAsync();
    }

    /// <summary>Publica como lo haría Reconstrucción: en el exchange de la plataforma, con su usuario y confirmación.</summary>
    public async Task PublicarComoReconstruccionAsync(IEnumerable<Sobre> sobres)
    {
        await using IConnection conexion = await ConectarAsync("reconstruccion");
        await using IChannel canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        long secuencia = 1;
        foreach (Sobre sobre in sobres)
        {
            Sobre numerado = sobre with { SecuenciaProductor = secuencia++ };
            BasicProperties propiedades = new() { MessageId = sobre.IdEvento.ToString(), Type = sobre.Tipo, DeliveryMode = DeliveryModes.Persistent };
            await canal.BasicPublishAsync(CatalogoEventos.Exchange, CatalogoEventos.PorTipo(sobre.Tipo).ClaveRuteo, true, propiedades,
                Encoding.UTF8.GetBytes(numerado.Serializar()));
        }
    }

    public async Task<uint> ListosAsync(string cola)
    {
        await using IConnection conexion = await ConectarAsync("admin");
        await using IChannel canal = await conexion.CreateChannelAsync();
        return (await canal.QueueDeclarePassiveAsync(cola)).MessageCount;
    }

    public async Task<List<Sobre>> VaciarAsync(string cola)
    {
        await using IConnection conexion = await ConectarAsync("admin");
        await using IChannel canal = await conexion.CreateChannelAsync();
        List<Sobre> sobres = [];
        while (await canal.BasicGetAsync(cola, autoAck: true) is { } mensaje)
        {
            sobres.Add(Sobre.Leer(mensaje.Body.Span));
        }
        return sobres;
    }

    public async Task LimpiarAsync()
    {
        foreach (string cola in new[] { "q.salida.se6", "dlq.salida.se6", "q.salida.se7", "dlq.salida.se7", "q.consulta-programa", "q.reconstruccion", "q.auditoria", "q.gestion-desastres" })
        {
            await VaciarAsync(cola);
        }
        await using NpgsqlConnection conexion = new(Dueno);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(
            "TRUNCATE se6.bandeja_salida, se6.contador_productor, se6.mensajes_procesados RESTART IDENTITY", conexion);
        await comando.ExecuteNonQueryAsync();
    }

    // ----------------------------------------------------------------- eventos

    public static readonly Guid IdDesastre = Guid.Parse("1b7d2f7e-3c1a-4c55-8e6f-2a9e1d0c4b11");

    public static Sobre ProyectoCreado(Guid proyecto, int i, decimal? presupuesto = 500_000_000m)
    {
        ResumenProyecto resumen = new(IdDesastre, $"Reconstrucción de la escuela {i}", EstadoProyecto.FORMULADO, presupuesto, 0m, 0m, 1);
        ProyectoCreadoV1 datos = new(proyecto, [Guid.NewGuid()], IdDesastre, presupuesto, "SGR", "ing.perez", null, ["63001"],
            AlcanceProyecto.MUNICIPAL, resumen.Nombre, resumen, "ungrd.operador");
        return Sobre.Crear(CatalogoEventos.ProyectoCreado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v1", datos,
            DateTimeOffset.UtcNow);
    }

    public static Sobre PresupuestoCambiado(Guid proyecto, int version, decimal nuevoPresupuesto, string fuente = "UNGRD")
    {
        ResumenProyecto resumen = new(IdDesastre, "Escuela", EstadoProyecto.APROBADO, nuevoPresupuesto, 0m, 0m, version);
        PresupuestoCambiadoV1 datos = new(proyecto, TipoCambioProyecto.PRESUPUESTO, 500_000_000m, nuevoPresupuesto, "SGR", fuente,
            version, "alcaldia.armenia", "Ajuste presupuestal", resumen);
        return Sobre.Crear(CatalogoEventos.PresupuestoCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v{version}", datos,
            DateTimeOffset.UtcNow);
    }

    public static Sobre CostoRegistrado(Guid proyecto, int version, decimal costoRegistrado, decimal costoAcumulado)
    {
        ResumenProyecto resumen = new(IdDesastre, "Escuela", EstadoProyecto.EN_EJECUCION, 600_000_000m, costoAcumulado, 50m, version);
        EstadoOAvanceCambiadoV1 datos = new(proyecto, TipoCambioProyecto.COSTO, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION,
            50m, costoAcumulado, costoRegistrado, null, costoAcumulado.ToString(System.Globalization.CultureInfo.InvariantCulture),
            version, "ing.perez", "Pago de acta", resumen);
        return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v{version}", datos,
            DateTimeOffset.UtcNow);
    }

    public static Sobre AvanceRegistrado(Guid proyecto, int version, decimal avance)
    {
        ResumenProyecto resumen = new(IdDesastre, "Escuela", EstadoProyecto.EN_EJECUCION, 600_000_000m, 0m, avance, version);
        EstadoOAvanceCambiadoV1 datos = new(proyecto, TipoCambioProyecto.AVANCE, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION,
            avance, 0m, null, "0", avance.ToString(System.Globalization.CultureInfo.InvariantCulture), version, "ing.perez",
            "Avance físico", resumen);
        return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v{version}", datos,
            DateTimeOffset.UtcNow);
    }

    public static Sobre ContratistaRegistrado(Guid proyecto, int version, string contratista)
    {
        ResumenProyecto resumen = new(IdDesastre, "Escuela", EstadoProyecto.EN_EJECUCION, 600_000_000m, 0m, 0m, version);
        EstadoOAvanceCambiadoV1 datos = new(proyecto, TipoCambioProyecto.CONTRATISTA, EstadoProyecto.APROBADO, EstadoProyecto.EN_EJECUCION,
            0m, 0m, null, null, contratista, version, "ing.perez", "Inicio de obra", resumen);
        return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v{version}", datos,
            DateTimeOffset.UtcNow);
    }

    public static Sobre EstadoCambiado(Guid proyecto, int version, EstadoProyecto anterior, EstadoProyecto nuevo, decimal avance = 100m, decimal costoAcumulado = 600_000_000m)
    {
        ResumenProyecto resumen = new(IdDesastre, "Escuela", nuevo, 600_000_000m, costoAcumulado, avance, version);
        EstadoOAvanceCambiadoV1 datos = new(proyecto, TipoCambioProyecto.ESTADO, anterior, nuevo, avance, costoAcumulado,
            null, anterior.ToString(), nuevo.ToString(), version, "ing.perez", "Cambio de estado", resumen);
        return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v{version}", datos,
            DateTimeOffset.UtcNow);
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
            await Task.Delay(200);
        }
        throw new TimeoutException($"No se cumplió en {segundos} s: {descripcion}");
    }

    public static Task EsperarAsync(Func<bool> condicion, string descripcion, int segundos = 60) =>
        EsperarAsync(() => Task.FromResult(condicion()), descripcion, segundos);

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
public sealed class ColeccionSe6 : ICollectionFixture<EntornoSe6>
{
    public const string Nombre = "se6";
}
