using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using RabbitMQ.Client;

namespace Plataforma.Comun.Adaptadores;

/// <summary>Mensajes que esperan ser entregados al sistema externo (campo mensajesEnCola de EV-23).</summary>
public interface IContadorMensajesEnCola
{
    Task<long> ContarAsync(CancellationToken ct);
}

/// <summary>Cuenta los mensajes listos de la cola de salida con una declaración pasiva (no necesita más permisos).</summary>
public sealed class ContadorColaBus : IContadorMensajesEnCola
{
    private readonly ConexionBus _bus;
    private readonly string _cola;

    public ContadorColaBus(ConexionBus bus, string cola)
    {
        _bus = bus;
        _cola = cola;
    }

    public async Task<long> ContarAsync(CancellationToken ct)
    {
        IConnection conexion = await _bus.ObtenerAsync(ct);
        await using IChannel canal = await conexion.CreateChannelAsync(cancellationToken: ct);
        QueueDeclareOk cola = await canal.QueueDeclarePassiveAsync(_cola, ct);
        return cola.MessageCount;
    }
}

/// <summary>Un adaptador sin cola de salida (solo entrada) informa 0 mensajes pendientes.</summary>
public sealed class ContadorSinCola : IContadorMensajesEnCola
{
    public Task<long> ContarAsync(CancellationToken ct) => Task.FromResult(0L);
}

/// <summary>EV-23 de una transición del cortacircuitos (E-15). Público para las pruebas del contrato.</summary>
public static class TraduccionEstadoIntegracion
{
    public static Sobre ASobre(TransicionCircuito transicion, string productor, long mensajesEnCola)
    {
        EstadoIntegracionCambiadoV1 datos = new(transicion.SistemaExterno, transicion.Anterior, transicion.Nuevo,
            transicion.Instante, Math.Max(0, mensajesEnCola));
        return Sobre.Crear(CatalogoEventos.EstadoIntegracionCambiado, productor, Clave(transicion), datos, transicion.Instante,
            idEvento: transicion.Id);
    }

    /// <summary>Cada transición es un hecho distinto: su identificador da la clave de idempotencia.</summary>
    public static string Clave(TransicionCircuito transicion) => $"integracion:{transicion.SistemaExterno}:{transicion.Id}";
}

/// <summary>
/// Publica en EV-23 cada apertura y cada cierre del cortacircuitos (DA-11, mitigación de ATAM R-10), por la bandeja de
/// salida del adaptador (AD-45): el relevo de comun lo entrega al bus con garantía. Consulta y Consulta del Programa lo
/// proyectan y la consola muestra "SE-n no responde desde hh:mm" (QA-01 b). Las transiciones se escriben en orden; si la
/// base no responde, se reintenta la misma hasta guardarla, para que ninguna se pierda.
/// </summary>
public sealed class PublicadorEstadoIntegracion<TContexto> : BackgroundService where TContexto : DbContext
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly MonitorCircuito _monitor;
    private readonly IContadorMensajesEnCola _contador;
    private readonly OpcionesBus _bus;
    private readonly ILogger<PublicadorEstadoIntegracion<TContexto>> _log;

    public PublicadorEstadoIntegracion(IServiceScopeFactory ambitos, MonitorCircuito monitor, IContadorMensajesEnCola contador,
        OpcionesBus bus, ILogger<PublicadorEstadoIntegracion<TContexto>> log)
    {
        _ambitos = ambitos;
        _monitor = monitor;
        _contador = contador;
        _bus = bus;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (TransicionCircuito transicion in _monitor.Transiciones.ReadAllAsync(ct))
            {
                long pendientes = await ContarAsync(ct);
                TimeSpan espera = TimeSpan.FromSeconds(1);
                while (true)
                {
                    try
                    {
                        await GuardarAsync(transicion, pendientes, ct);
                        _log.LogWarning("Integración con {Sistema}: {Anterior} -> {Nuevo} ({Pendientes} mensajes en cola). EV-23 en la bandeja.",
                            transicion.SistemaExterno, transicion.Anterior, transicion.Nuevo, pendientes);
                        break;
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        _log.LogWarning(e, "No se pudo guardar EV-23 de {Sistema}; se reintenta.", transicion.SistemaExterno);
                        await Task.Delay(espera, ct);
                        espera = TimeSpan.FromSeconds(Math.Min(30, espera.TotalSeconds * 2));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // apagado
        }
    }

    private async Task<long> ContarAsync(CancellationToken ct)
    {
        try
        {
            using CancellationTokenSource plazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            plazo.CancelAfter(TimeSpan.FromSeconds(3));
            return await _contador.ContarAsync(plazo.Token);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // El conteo es informativo: sin él, el evento sale igual (el estado es lo que importa para QA-01 b).
            _log.LogDebug(e, "No se pudo contar la cola de salida.");
            return 0;
        }
    }

    private async Task GuardarAsync(TransicionCircuito transicion, long pendientes, CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        TContexto db = ambito.ServiceProvider.GetRequiredService<TContexto>();
        string clave = TraduccionEstadoIntegracion.Clave(transicion);
        if (await db.Set<FilaBandeja>().AnyAsync(b => b.ClaveIdempotencia == clave, ct))
        {
            return; // un reintento después de un guardado que sí llegó a la base
        }
        ambito.ServiceProvider.GetRequiredService<EscritorBandeja<TContexto>>()
            .Agregar(TraduccionEstadoIntegracion.ASobre(transicion, _bus.Productor, pendientes));
        await db.SaveChangesAsync(ct);
    }
}
