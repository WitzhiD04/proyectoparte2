using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AdaptadorSe6;

/// <summary>
/// Agrupa elementos de un canal en lotes acotados por tamaño máximo o por ventana de tiempo desde que llega el primer
/// elemento del lote (DA-11, Cuadro 14: "envío por lote").
/// </summary>
public static class ColectorLotes
{
    public static async Task<IReadOnlyList<T>> TomarLoteAsync<T>(
        ChannelReader<T> lector,
        int tamanoMaximo,
        TimeSpan ventana,
        TimeProvider reloj,
        CancellationToken ct)
    {
        int limite = Math.Max(1, tamanoMaximo);
        T primero = await lector.ReadAsync(ct);
        List<T> lote = [primero];

        while (lote.Count < limite && lector.TryRead(out T? inmediato))
        {
            lote.Add(inmediato);
        }
        if (lote.Count >= limite || ventana <= TimeSpan.Zero)
        {
            return lote;
        }

        using CancellationTokenSource plazo = new();
        await using ITimer temporizador = reloj.CreateTimer(static estado => ((CancellationTokenSource)estado!).Cancel(),
            plazo, ventana, Timeout.InfiniteTimeSpan);
        using CancellationTokenSource combinado = CancellationTokenSource.CreateLinkedTokenSource(ct, plazo.Token);

        try
        {
            while (lote.Count < limite)
            {
                if (!await lector.WaitToReadAsync(combinado.Token))
                {
                    break;
                }
                while (lote.Count < limite && lector.TryRead(out T? siguiente))
                {
                    lote.Add(siguiente);
                }
            }
        }
        catch (OperationCanceledException) when (plazo.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Se cumplió la ventana de tiempo antes de llenar el lote: se entrega con los elementos acumulados.
        }

        return lote;
    }
}

/// <summary>
/// Entrega un lote de eventos canónicos a SE-6 con la misma regla de indisponibilidad que <see cref="ManejadorSalida"/>
/// de comun (AD-36): si SE-6 no responde, no lanza (evita que los mensajes vayan a la DLQ); espera y reintenta hasta
/// que <see cref="CompuertaLotesSe6"/> pausa el consumidor cuando el circuito se abre. Si SE-6 rechaza el lote (4xx),
/// lanza <see cref="EventoInvalidoException"/> para apartarlo a la cola de mensajes muertos.
/// </summary>
public sealed class ManejadorLotesSe6
{
    private readonly ISalidaLotesSe6 _adaptador;
    private readonly MonitorCircuito _monitor;
    private readonly OpcionesSalida _opciones;
    private readonly ILogger<ManejadorLotesSe6> _log;

    public ManejadorLotesSe6(ISalidaLotesSe6 adaptador, MonitorCircuito monitor, OpcionesSalida opciones,
        ILogger<ManejadorLotesSe6> log)
    {
        _adaptador = adaptador;
        _monitor = monitor;
        _opciones = opciones;
        _log = log;
    }

    public async Task ManejarLoteAsync(IReadOnlyList<Sobre> sobres, CancellationToken ct)
    {
        if (sobres.Count == 0)
        {
            return;
        }
        TimeSpan espera = TimeSpan.FromSeconds(Math.Max(0.05, _opciones.EsperaReintentoSegundos));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ResultadoEntrega resultado = await _adaptador.EntregarLoteAsync(sobres, ct);
                _log.LogInformation("Lote de {Cantidad} eventos a {Sistema}: {Resultado}.", sobres.Count,
                    _adaptador.SistemaExterno, resultado);
                return;
            }
            catch (EntregaRechazadaException e)
            {
                throw new EventoInvalidoException($"{_adaptador.SistemaExterno} rechazó el lote: {e.Message}", e);
            }
            catch (Exception e) when (Indisponibilidad.Es(e, ct))
            {
                _log.LogWarning("{Sistema} no responde ({Error}); el lote de {Cantidad} eventos sigue pendiente.",
                    _adaptador.SistemaExterno, e.GetType().Name, sobres.Count);
                await _monitor.EsperarCambioAsync(espera, ct);
            }
        }
    }
}

/// <summary>
/// Consumidor por lotes de <c>q.salida.se6</c> (DA-11, AD-36). Toma hasta <see cref="OpcionesSe6.TamanoLote"/> mensajes
/// (o los que lleguen dentro de <see cref="OpcionesSe6.VentanaLoteSegundos"/>), entrega el lote a SE-6, registra los
/// mensajes procesados en <c>se6.mensajes_procesados</c> en una transacción y solo entonces confirma (ACK) los mensajes
/// en RabbitMQ. Si el circuito se abre y la compuerta detiene este consumidor, el canal se cierra sin confirmar ni
/// rechazar el lote en curso: todos sus mensajes vuelven listos a <c>q.salida.se6</c> y ninguno va a <c>dlq.salida.se6</c>.
/// </summary>
public sealed class ConsumidorLotesSe6 : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly ConexionBus _bus;
    private readonly OpcionesBus _opcionesBus;
    private readonly OpcionesSe6 _opcionesSe6;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ConsumidorLotesSe6> _log;
    private readonly ValidadorContrato _validador = ValidadorContrato.Instancia;

    public ConsumidorLotesSe6(
        IServiceScopeFactory ambitos,
        ConexionBus bus,
        OpcionesBus opcionesBus,
        OpcionesSe6 opcionesSe6,
        TimeProvider reloj,
        ILogger<ConsumidorLotesSe6> log)
    {
        if (string.IsNullOrWhiteSpace(opcionesBus.Cola))
        {
            throw new InvalidOperationException("Falta Bus:Cola: el consumidor necesita el nombre de su cola.");
        }
        _ambitos = ambitos;
        _bus = bus;
        _opcionesBus = opcionesBus;
        _opcionesSe6 = opcionesSe6;
        _reloj = reloj;
        _log = log;
    }

    private sealed record EntregaRecibida(ulong DeliveryTag, string? MessageId, long EntregasPrevias, byte[] Cuerpo);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int tamanoLote = Math.Max(1, _opcionesSe6.TamanoLote);
        TimeSpan ventana = TimeSpan.FromSeconds(Math.Max(0.01, _opcionesSe6.VentanaLoteSegundos));
        ushort prefetch = (ushort)Math.Clamp(tamanoLote, 1, 1000);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                IConnection conexion = await _bus.ObtenerAsync(ct);
                await using IChannel canal = await conexion.CreateChannelAsync(cancellationToken: ct);
                await canal.QueueDeclarePassiveAsync(_opcionesBus.Cola!, ct);
                await canal.BasicQosAsync(0, prefetch, false, ct);

                Channel<EntregaRecibida> entregas = Channel.CreateUnbounded<EntregaRecibida>();
                AsyncEventingBasicConsumer consumidor = new(canal);
                consumidor.ReceivedAsync += (_, entrega) =>
                {
                    // RabbitMQ.Client 7 devuelve el buffer de Body al pool al terminar ReceivedAsync: se copia aquí.
                    entregas.Writer.TryWrite(new EntregaRecibida(
                        entrega.DeliveryTag,
                        entrega.BasicProperties.MessageId,
                        ContarEntregasPrevias(entrega),
                        entrega.Body.ToArray()));
                    return Task.CompletedTask;
                };
                await canal.BasicConsumeAsync(_opcionesBus.Cola!, autoAck: false, consumidor, ct);
                _log.LogInformation("Consumiendo lotes de la cola {Cola} (tamaño {Tamano}, ventana {Ventana}s).",
                    _opcionesBus.Cola, tamanoLote, ventana.TotalSeconds);

                while (canal.IsOpen && !ct.IsCancellationRequested)
                {
                    IReadOnlyList<EntregaRecibida> lote = await ColectorLotes.TomarLoteAsync(
                        entregas.Reader, tamanoLote, ventana, _reloj, ct);
                    await ProcesarLoteEntregasAsync(canal, lote, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "El consumidor por lotes de {Cola} perdió la conexión con el bus; se reconecta.",
                    _opcionesBus.Cola);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task ProcesarLoteEntregasAsync(IChannel canal, IReadOnlyList<EntregaRecibida> entregas, CancellationToken ct)
    {
        List<(EntregaRecibida Entrega, Sobre Sobre)> validos = new(entregas.Count);
        foreach (EntregaRecibida entrega in entregas)
        {
            try
            {
                Sobre sobre = Sobre.Leer(entrega.Cuerpo);
                _validador.Validar(sobre);
                validos.Add((entrega, sobre));
            }
            catch (Exception e) when (e is JsonException or EventoInvalidoException or NotSupportedException)
            {
                _log.LogError(e, "Mensaje {Id} de {Cola} no cumple el contrato; se aparta a la cola de mensajes muertos.",
                    entrega.MessageId, _opcionesBus.Cola);
                await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, CancellationToken.None);
            }
        }

        if (validos.Count == 0)
        {
            return;
        }

        try
        {
            await AplicarLoteAsync(validos.Select(v => v.Sobre).ToList(), ct);
            foreach ((EntregaRecibida entrega, _) in validos)
            {
                await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, CancellationToken.None);
            }
        }
        catch (EventoInvalidoException e)
        {
            _log.LogError(e, "Lote rechazado por SE-6; se aparta a la cola de mensajes muertos.");
            foreach ((EntregaRecibida entrega, _) in validos)
            {
                await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, CancellationToken.None);
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            foreach ((EntregaRecibida entrega, Sobre sobre) in validos)
            {
                long intento = entrega.EntregasPrevias + 1;
                if (intento >= _opcionesBus.LimiteEntregas)
                {
                    _log.LogError(e, "El evento {Tipo} {Id} falló {Intentos} veces; se aparta a la cola de mensajes muertos.",
                        sobre.Tipo, sobre.IdEvento, intento);
                    await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, CancellationToken.None);
                }
                else
                {
                    _log.LogWarning(e, "No se pudo aplicar el evento {Tipo} {Id} (intento {Intento} de {Limite}); se reencola.",
                        sobre.Tipo, sobre.IdEvento, intento, _opcionesBus.LimiteEntregas);
                    await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
                }
            }
        }
    }

    /// <summary>
    /// Filtra los eventos ya procesados, entrega el lote de los nuevos a SE-6 y los registra en <c>se6.mensajes_procesados</c>
    /// dentro de una transacción. Devuelve cuántos eventos nuevos se procesaron.
    /// </summary>
    public async Task<int> AplicarLoteAsync(IReadOnlyList<Sobre> sobres, CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        AdaptadorSe6DbContext db = ambito.ServiceProvider.GetRequiredService<AdaptadorSe6DbContext>();
        ManejadorLotesSe6 manejador = ambito.ServiceProvider.GetRequiredService<ManejadorLotesSe6>();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        List<Sobre> unicos = sobres.DistinctBy(s => s.ClaveIdempotencia, StringComparer.Ordinal).ToList();
        List<string> claves = unicos.Select(s => s.ClaveIdempotencia).ToList();
        HashSet<string> yaProcesados = (await db.Procesados
            .Where(m => claves.Contains(m.ClaveIdempotencia))
            .Select(m => m.ClaveIdempotencia)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        List<Sobre> nuevos = unicos.Where(s => !yaProcesados.Contains(s.ClaveIdempotencia)).ToList();
        if (nuevos.Count == 0)
        {
            await transaccion.CommitAsync(ct);
            return 0;
        }

        await manejador.ManejarLoteAsync(nuevos, ct);

        DateTimeOffset ahora = _reloj.GetUtcNow();
        foreach (Sobre sobre in nuevos)
        {
            db.Procesados.Add(new FilaMensajeProcesado
            {
                ClaveIdempotencia = sobre.ClaveIdempotencia,
                IdEvento = sobre.IdEvento,
                Tipo = sobre.Tipo,
                ProcesadoEn = ahora,
            });
        }

        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return nuevos.Count;
    }

    private static long ContarEntregasPrevias(BasicDeliverEventArgs entrega)
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
}

/// <summary>
/// Compuerta del consumo por lotes de <c>q.salida.se6</c> (AD-36, Cuadro 5, QA-01 c y d): igual que
/// <see cref="CompuertaConsumo{TContexto}"/> de comun, arranca <see cref="ConsumidorLotesSe6"/> con el circuito cerrado
/// y lo detiene cuando se abre; mientras está abierto sondea SE-6 (<c>GET /v1/salud</c>) y, al cerrarse el circuito,
/// vuelve a arrancar el consumidor para entregar los lotes pendientes.
/// </summary>
public sealed class CompuertaLotesSe6 : BackgroundService
{
    private readonly IServiceProvider _servicios;
    private readonly MonitorCircuito _monitor;
    private readonly OpcionesSalida _opciones;
    private readonly ILogger<CompuertaLotesSe6> _log;
    private ConsumidorLotesSe6? _consumidor;

    public CompuertaLotesSe6(IServiceProvider servicios, MonitorCircuito monitor, OpcionesSalida opciones,
        ILogger<CompuertaLotesSe6> log)
    {
        _servicios = servicios;
        _monitor = monitor;
        _opciones = opciones;
        _log = log;
    }

    /// <summary>El consumidor por lotes está tomando mensajes de la cola.</summary>
    public bool Consumiendo => _consumidor is not null;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        TimeSpan sondeo = TimeSpan.FromSeconds(Math.Max(0.1, _opciones.IntervaloSondeoSegundos));
        try
        {
            while (!ct.IsCancellationRequested)
            {
                EstadoIntegracion estado = _monitor.Estado;
                bool abierto = estado == EstadoIntegracion.ABIERTO;
                if (!abierto && _consumidor is null)
                {
                    _consumidor = ActivatorUtilities.CreateInstance<ConsumidorLotesSe6>(_servicios);
                    await _consumidor.StartAsync(ct);
                    _log.LogInformation("{Sistema} responde: se consume la cola de salida por lotes.", _monitor.SistemaExterno);
                    continue;
                }
                if (abierto && _consumidor is not null)
                {
                    await DetenerAsync();
                    _log.LogWarning("{Sistema} no responde: se pausa el consumo; los movimientos esperan en la cola de salida.",
                        _monitor.SistemaExterno);
                    continue;
                }
                if (abierto)
                {
                    await SondearAsync(ct);
                    await _monitor.EsperarCambioAsync(estado, sondeo, ct);
                    continue;
                }
                await _monitor.EsperarCambioAsync(estado, Timeout.InfiniteTimeSpan, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // apagado
        }
        finally
        {
            await DetenerAsync();
        }
    }

    private async Task SondearAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope ambito = _servicios.CreateScope();
            await ambito.ServiceProvider.GetRequiredService<ISondaSistemaExterno>().SondearAsync(ct);
        }
        catch (Exception e) when (Indisponibilidad.Es(e, ct))
        {
            _log.LogDebug("Sondeo de {Sistema}: sigue sin responder ({Error}).", _monitor.SistemaExterno, e.GetType().Name);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(e, "Sondeo de {Sistema} con un error inesperado.", _monitor.SistemaExterno);
        }
    }

    private async Task DetenerAsync()
    {
        ConsumidorLotesSe6? consumidor = _consumidor;
        _consumidor = null;
        if (consumidor is null)
        {
            return;
        }
        try
        {
            using CancellationTokenSource plazo = new(TimeSpan.FromSeconds(30));
            await consumidor.StopAsync(plazo.Token);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "El consumidor por lotes de la cola de salida no se detuvo limpiamente.");
        }
        finally
        {
            consumidor.Dispose();
        }
    }
}
