using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Infraestructura.Persistencia;

namespace Reconstruccion.Infraestructura.Mensajeria;

public sealed class OpcionesRabbit
{
    public string Host { get; set; } = "localhost";
    public int Puerto { get; set; } = 5672;
    public string Usuario { get; set; } = "guest";
    public string Clave { get; set; } = "guest";
    public string Intercambio { get; set; } = "reconstruccion.eventos";
    public string ColaProyeccion { get; set; } = "programa.proyeccion";

    public static OpcionesRabbit Leer(IConfiguration configuracion)
    {
        OpcionesRabbit opciones = new();
        configuracion.GetSection("RabbitMq").Bind(opciones);
        return opciones;
    }

    public ConnectionFactory Fabrica()
    {
        return new ConnectionFactory { HostName = Host, Port = Puerto, UserName = Usuario, Password = Clave };
    }
}

/// <summary>
/// Relevo de eventos: lee la bandeja de salida y publica en RabbitMQ lo que esté pendiente.
/// Marca un mensaje como publicado solo cuando el bus confirma que lo recibió. Si el bus está caído,
/// los mensajes se quedan en la bandeja y se reintenta en la siguiente vuelta.
/// </summary>
public sealed class RelevoEventos : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly OpcionesRabbit _opciones;
    private readonly ILogger<RelevoEventos> _log;
    private readonly TimeSpan _intervalo;
    private readonly int _tamanoLote;
    private IConnection? _conexion;
    private IChannel? _canal;

    public RelevoEventos(IServiceScopeFactory ambitos, IConfiguration configuracion, ILogger<RelevoEventos> log)
    {
        _ambitos = ambitos;
        _opciones = OpcionesRabbit.Leer(configuracion);
        _log = log;
        _intervalo = TimeSpan.FromSeconds(configuracion.GetValue("Relevo:IntervaloSegundos", 2));
        _tamanoLote = configuracion.GetValue("Relevo:TamanoLote", 50);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int publicados = await PublicarPendientesAsync(ct);
                if (publicados == _tamanoLote)
                {
                    continue; // puede haber más: no esperar
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "El relevo no pudo publicar; los eventos siguen en la bandeja y se reintenta.");
                await CerrarCanalAsync();
            }
            await Task.Delay(_intervalo, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
        await CerrarCanalAsync();
    }

    private async Task<int> PublicarPendientesAsync(CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        ReconstruccionDbContext db = ambito.ServiceProvider.GetRequiredService<ReconstruccionDbContext>();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: si hay varias copias del servicio, cada una toma mensajes distintos.
        List<MensajeSalidaFila> pendientes = await db.BandejaSalida
            .FromSqlInterpolated($@"SELECT * FROM bandeja_salida WHERE publicado_en IS NULL
                                    ORDER BY secuencia LIMIT {_tamanoLote} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (pendientes.Count == 0)
        {
            return 0;
        }

        IChannel canal = await CanalAsync(ct);
        foreach (MensajeSalidaFila mensaje in pendientes)
        {
            BasicProperties propiedades = new()
            {
                MessageId = mensaje.ClaveIdempotencia,
                Type = mensaje.Tipo,
                ContentType = "application/json",
                Persistent = true,
            };
            // Con confirmaciones activas, esta llamada termina cuando el bus acepta el mensaje.
            await canal.BasicPublishAsync(_opciones.Intercambio, mensaje.Tipo, false, propiedades,
                Encoding.UTF8.GetBytes(mensaje.Contenido), ct);
            mensaje.PublicadoEn = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return pendientes.Count;
    }

    private async Task<IChannel> CanalAsync(CancellationToken ct)
    {
        if (_canal is not null && _canal.IsOpen)
        {
            return _canal;
        }
        await CerrarCanalAsync();
        _conexion = await _opciones.Fabrica().CreateConnectionAsync(ct);
        _canal = await _conexion.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
        await _canal.ExchangeDeclareAsync(_opciones.Intercambio, ExchangeType.Topic, true, false, cancellationToken: ct);
        return _canal;
    }

    private async Task CerrarCanalAsync()
    {
        try
        {
            if (_canal is not null) await _canal.DisposeAsync();
            if (_conexion is not null) await _conexion.DisposeAsync();
        }
        catch (Exception)
        {
            // la conexión ya estaba caída
        }
        _canal = null;
        _conexion = null;
    }
}

/// <summary>
/// Consumidor del servicio de Consulta del Programa: recibe los eventos de los proyectos y actualiza
/// el modelo de lectura. Confirma el mensaje solo después de aplicarlo.
/// </summary>
public sealed class ConsumidorProyeccion : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly OpcionesRabbit _opciones;
    private readonly ILogger<ConsumidorProyeccion> _log;

    public ConsumidorProyeccion(IServiceScopeFactory ambitos, IConfiguration configuracion, ILogger<ConsumidorProyeccion> log)
    {
        _ambitos = ambitos;
        _opciones = OpcionesRabbit.Leer(configuracion);
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using IConnection conexion = await _opciones.Fabrica().CreateConnectionAsync(ct);
                await using IChannel canal = await conexion.CreateChannelAsync(cancellationToken: ct);
                await canal.ExchangeDeclareAsync(_opciones.Intercambio, ExchangeType.Topic, true, false, cancellationToken: ct);
                // Cola de tipo quorum: se replica entre los nodos de RabbitMQ.
                Dictionary<string, object?> argumentos = new() { { "x-queue-type", "quorum" } };
                await canal.QueueDeclareAsync(_opciones.ColaProyeccion, true, false, false, argumentos, cancellationToken: ct);
                await canal.QueueBindAsync(_opciones.ColaProyeccion, _opciones.Intercambio, "proyecto.#", cancellationToken: ct);
                await canal.BasicQosAsync(0, 20, false, ct);

                AsyncEventingBasicConsumer consumidor = new(canal);
                consumidor.ReceivedAsync += async (_, entrega) =>
                {
                    try
                    {
                        string contenido = Encoding.UTF8.GetString(entrega.Body.Span);
                        using IServiceScope ambito = _ambitos.CreateScope();
                        ActualizarProyeccionPrograma casoDeUso = ambito.ServiceProvider.GetRequiredService<ActualizarProyeccionPrograma>();
                        await casoDeUso.EjecutarAsync(contenido, ct);
                        await canal.BasicAckAsync(entrega.DeliveryTag, false, ct);
                    }
                    catch (Exception e)
                    {
                        _log.LogWarning(e, "No se pudo aplicar un evento a la proyección; se reintenta.");
                        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                        await canal.BasicNackAsync(entrega.DeliveryTag, false, true, CancellationToken.None);
                    }
                };
                await canal.BasicConsumeAsync(_opciones.ColaProyeccion, false, consumidor, ct);
                _log.LogInformation("Consumidor de la proyección conectado a la cola {Cola}.", _opciones.ColaProyeccion);

                while (canal.IsOpen && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "El consumidor de la proyección perdió la conexión con el bus; se reconecta.");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
