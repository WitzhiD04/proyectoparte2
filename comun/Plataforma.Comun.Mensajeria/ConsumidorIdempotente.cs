using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Comun.Mensajeria;

/// <summary>
/// Puerto de entrada de un servicio para los eventos del bus. Lo implementa un adaptador de la capa de
/// infraestructura, que traduce el sobre canónico a un comando o caso de uso de la aplicación.
/// </summary>
public interface IManejadorEvento
{
    /// <summary>Aplica el evento. Los tipos que el servicio no maneja se ignoran (la cola puede recibir más tipos).</summary>
    Task ManejarAsync(Sobre sobre, CancellationToken ct);
}

/// <summary>
/// Consumidor de la cola de un servicio (AD-02, DA-03). Por cada mensaje:
/// valida el sobre contra el contrato; en una transacción revisa si ya lo procesó, aplica el efecto y lo registra
/// como procesado; luego confirma. Un evento repetido se confirma sin efecto (consumo idempotente).
/// Un error permanente (mensaje que no cumple el contrato) va directo a la cola de mensajes muertos; un error
/// transitorio se reencola y, al superar el límite de entregas de la cola, también se aparta allí. Así un mensaje
/// envenenado no bloquea la cola (corrige H-42).
/// </summary>
public sealed class ConsumidorIdempotente<TContexto> : BackgroundService where TContexto : DbContext
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly ConexionBus _bus;
    private readonly OpcionesBus _opciones;
    private readonly ILogger<ConsumidorIdempotente<TContexto>> _log;
    private readonly ValidadorContrato _validador = ValidadorContrato.Instancia;

    public ConsumidorIdempotente(IServiceScopeFactory ambitos, ConexionBus bus, OpcionesBus opciones,
        ILogger<ConsumidorIdempotente<TContexto>> log)
    {
        if (string.IsNullOrWhiteSpace(opciones.Cola))
        {
            throw new InvalidOperationException("Falta Bus:Cola: el consumidor necesita el nombre de su cola.");
        }
        _ambitos = ambitos;
        _bus = bus;
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                IConnection conexion = await _bus.ObtenerAsync(ct);
                await using IChannel canal = await conexion.CreateChannelAsync(cancellationToken: ct);
                // Declaración pasiva: la cola debe existir porque la topología se declara al desplegar.
                await canal.QueueDeclarePassiveAsync(_opciones.Cola!, ct);
                await canal.BasicQosAsync(0, _opciones.Prefetch, false, ct);

                AsyncEventingBasicConsumer consumidor = new(canal);
                consumidor.ReceivedAsync += (_, entrega) => AtenderAsync(canal, entrega, ct);
                await canal.BasicConsumeAsync(_opciones.Cola!, autoAck: false, consumidor, ct);
                _log.LogInformation("Consumiendo de la cola {Cola}.", _opciones.Cola);

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
                _log.LogWarning(e, "El consumidor de {Cola} perdió la conexión con el bus; se reconecta.", _opciones.Cola);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task AtenderAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        Sobre sobre;
        try
        {
            sobre = Sobre.Leer(entrega.Body.Span);
            if (!_opciones.AceptarTiposDesconocidos)
            {
                _validador.Validar(sobre);
            }
            else if (!_validador.ValidarConocidos(sobre))
            {
                _log.LogWarning("El evento {Tipo} v{Version} no está en el contrato desplegado; se entrega sin validar sus datos (E-34).",
                    sobre.Tipo, sobre.Version);
            }
        }
        catch (Exception e) when (e is JsonException or EventoInvalidoException or NotSupportedException)
        {
            _log.LogError(e, "Mensaje {Id} de {Cola} no cumple el contrato; se aparta a la cola de mensajes muertos.",
                entrega.BasicProperties.MessageId, _opciones.Cola);
            await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, CancellationToken.None);
            return;
        }

        try
        {
            bool aplicado = await AplicarAsync(sobre, ct);
            await canal.BasicAckAsync(entrega.DeliveryTag, false, CancellationToken.None);
            if (!aplicado)
            {
                _log.LogDebug("Evento {Clave} ya procesado; se descarta.", sobre.ClaveIdempotencia);
            }
        }
        catch (EventoInvalidoException e)
        {
            _log.LogError(e, "Evento {Tipo} {Id} rechazado por el servicio; se aparta a la cola de mensajes muertos.",
                sobre.Tipo, sobre.IdEvento);
            await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, CancellationToken.None);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            long intento = EntregasPrevias(entrega) + 1;
            if (intento >= _opciones.LimiteEntregas)
            {
                _log.LogError(e, "El evento {Tipo} {Id} falló {Intentos} veces; se aparta a la cola de mensajes muertos.",
                    sobre.Tipo, sobre.IdEvento, intento);
                await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, CancellationToken.None);
                return;
            }
            _log.LogWarning(e, "No se pudo aplicar el evento {Tipo} {Id} (intento {Intento} de {Limite}); se reencola.",
                sobre.Tipo, sobre.IdEvento, intento, _opciones.LimiteEntregas);
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            await canal.BasicNackAsync(entrega.DeliveryTag, false, requeue: true, CancellationToken.None);
        }
    }

    /// <summary>
    /// Entregas anteriores de este mensaje, según el broker. En RabbitMQ 4 un nack con reencolado de AMQP 0-9-1 no
    /// cuenta para el delivery-limit de la cola quorum, así que el consumidor cuenta los intentos con x-acquired-count
    /// (o x-delivery-count) y aparta el mensaje él mismo al llegar al límite. Sin encabezados, usa la marca de reentrega.
    /// </summary>
    private static long EntregasPrevias(BasicDeliverEventArgs entrega)
    {
        IDictionary<string, object?>? encabezados = entrega.BasicProperties.Headers;
        foreach (string nombre in new[] { "x-acquired-count", "x-delivery-count" })
        {
            if (encabezados is not null && encabezados.TryGetValue(nombre, out object? valor) && valor is not null)
            {
                return Convert.ToInt64(valor);
            }
        }
        return entrega.Redelivered ? 1 : 0;
    }

    /// <summary>Aplica el evento una sola vez. Devuelve falso si ya estaba procesado.</summary>
    public async Task<bool> AplicarAsync(Sobre sobre, CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        TContexto db = ambito.ServiceProvider.GetRequiredService<TContexto>();
        IManejadorEvento manejador = ambito.ServiceProvider.GetRequiredService<IManejadorEvento>();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        bool yaProcesado = await db.Set<FilaMensajeProcesado>().AnyAsync(m => m.ClaveIdempotencia == sobre.ClaveIdempotencia, ct);
        if (yaProcesado)
        {
            await transaccion.CommitAsync(ct);
            return false;
        }
        await manejador.ManejarAsync(sobre, ct);
        db.Set<FilaMensajeProcesado>().Add(new FilaMensajeProcesado
        {
            ClaveIdempotencia = sobre.ClaveIdempotencia,
            IdEvento = sobre.IdEvento,
            Tipo = sobre.Tipo,
            ProcesadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return true;
    }
}
