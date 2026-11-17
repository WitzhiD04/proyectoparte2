using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using RabbitMQ.Client;
using SimSe7;
using Testcontainers.PostgreSql;

namespace AdaptadorSe7.Integracion;

/// <summary>El simulador de SE-7 en memoria, sin generación automática (las pruebas deciden cada cambio).</summary>
public sealed class SimuladorSe7 : WebApplicationFactory<MarcadorSimSe7>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Simulador:IntervaloGeneracionSegundos", "0");
        // En modo lento responde después de 30 s: mucho más que el tiempo de espera del adaptador en las pruebas.
        builder.UseSetting("Simulador:RetrasoLentoMs", "30000");
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
    }

    public RegistroSe7 Registro => Services.GetRequiredService<RegistroSe7>();
}

/// <summary>
/// PostgreSQL 18 con la base "adaptadores" migrada por el migrador del adaptador (DT-19) y RabbitMQ 4.4 con la topología
/// real de despliegue/bus/definiciones.json (un nodo), como en comun. Se levantan una vez para toda la colección.
/// </summary>
public sealed class EntornoSe7 : IAsyncLifetime
{
    public const string ClaveApp = "adaptador_se7_prueba";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4").Build();
    private readonly IContainer _rabbit;

    public EntornoSe7()
    {
        string bus = Path.Combine(RaizRepositorio(), "despliegue", "bus");
        _rabbit = new ContainerBuilder("rabbitmq:4.4.0-management")
            .WithPortBinding(5672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "definiciones.json")), "/etc/rabbitmq/definiciones.json")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(bus, "rabbitmq-un-nodo.conf")), "/etc/rabbitmq/rabbitmq.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
    }

    public string Dueno => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = MigracionAdaptadorSe7.Base }.ConnectionString;
    public string App => new NpgsqlConnectionStringBuilder(Dueno) { Username = MigracionAdaptadorSe7.RolApp, Password = ClaveApp }.ConnectionString;
    public string NodoBus => $"{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";

    public IConfiguration ConfiguracionMigrador => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:AdaptadorSe7Dueno"] = Dueno,
        ["Migracion:ClaveAdaptadorSe7App"] = ClaveApp,
    }).Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await MigracionAdaptadorSe7.EjecutarAsync(ConfiguracionMigrador, NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public AdaptadorSe7DbContext ComoApp() => new(MigracionAdaptadorSe7.Opciones(App));

    /// <summary>
    /// Un proceso del adaptador en el modo dado, con el mismo cableado que Program.cs, tiempos cortos y SE-7 en memoria.
    /// </summary>
    public IHost Adaptador(string modo, SimuladorSe7 se7, Dictionary<string, string?>? extra = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        Dictionary<string, string?> valores = RegistroAdaptadorSe7.ValoresPorOmision(modo);
        valores["ConnectionStrings:AdaptadorSe7"] = App;
        valores["Bus:Nodos:0"] = NodoBus;
        valores["Bus:Clave"] = "adaptador_se7_local";
        valores["Bus:IntervaloRelevoMs"] = "100";
        valores["Se7:Url"] = "http://localhost/";
        valores["Resiliencia:TiempoEsperaIntentoSegundos"] = "0.3";
        valores["Resiliencia:TiempoEsperaTotalSegundos"] = "5";
        valores["Resiliencia:EsperaBaseReintentoSegundos"] = "0.05";
        valores["Resiliencia:DuracionAperturaSegundos"] = "1";
        valores["Salida:EsperaReintentoSegundos"] = "0.2";
        valores["Salida:IntervaloSondeoSegundos"] = "0.5";
        valores["Entrada:IntervaloSegundos"] = "1";
        foreach ((string clave, string? valor) in extra ?? [])
        {
            valores[clave] = valor;
        }
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(valores);
        builder.Logging.ClearProviders();
        if (modo == "salida")
        {
            builder.Services.AgregarModoSalida(builder.Configuration);
        }
        else
        {
            builder.Services.AgregarModoEntrada(builder.Configuration);
        }
        // SE-7 es el simulador en memoria: el cliente HTTP real, con su resiliencia, sobre el servidor de pruebas.
        builder.Services.AddHttpClient<ClienteSe7>()
            .ConfigurePrimaryHttpMessageHandler(() => new ClienteQueAbandona { InnerHandler = se7.Server.CreateHandler() });
        return builder.Build();
    }

    /// <summary>
    /// El servidor de pruebas no avisa al servidor cuando el cliente cancela, y el tiempo de espera de Polly v8 es
    /// cooperativo: espera a que la llamada termine. Por la red, el cliente abandona la conexión al vencer el plazo; este
    /// manejador hace lo mismo, para que el modo "lento" del simulador agote los tiempos de espera como en compose.
    /// </summary>
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

    /// <summary>Mensajes listos de una cola (declaración pasiva: el dato es inmediato, sin la demora de las estadísticas).</summary>
    public async Task<uint> ListosAsync(string cola)
    {
        await using IConnection conexion = await ConectarAsync("admin");
        await using IChannel canal = await conexion.CreateChannelAsync();
        return (await canal.QueueDeclarePassiveAsync(cola)).MessageCount;
    }

    /// <summary>Saca y devuelve todos los mensajes de una cola (como administrador).</summary>
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
        foreach (string cola in new[] { "q.salida.se7", "dlq.salida.se7", "q.consulta-programa", "q.reconstruccion", "q.auditoria", "q.gestion-desastres" })
        {
            await VaciarAsync(cola);
        }
        await using NpgsqlConnection conexion = new(Dueno);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(
            "TRUNCATE se7.bandeja_salida, se7.contador_productor, se7.mensajes_procesados, se7.marcas_avance RESTART IDENTITY", conexion);
        await comando.ExecuteNonQueryAsync();
    }

    // ----------------------------------------------------------------- eventos

    public static Sobre ProyectoCreado(Guid proyecto, int i)
    {
        Guid desastre = Guid.Parse("1b7d2f7e-3c1a-4c55-8e6f-2a9e1d0c4b11");
        ResumenProyecto resumen = new(desastre, $"Reconstrucción de la escuela {i}", EstadoProyecto.FORMULADO, null, 0m, 0m, 1);
        ProyectoCreadoV1 datos = new(proyecto, [Guid.NewGuid()], desastre, null, null, null, null, ["63001"], AlcanceProyecto.MUNICIPAL,
            resumen.Nombre, resumen, "ungrd.operador");
        return Sobre.Crear(CatalogoEventos.ProyectoCreado, CatalogoEventos.Reconstruccion, $"proyecto:{proyecto}:v1", datos,
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
public sealed class ColeccionSe7 : ICollectionFixture<EntornoSe7>
{
    public const string Nombre = "se7";
}
