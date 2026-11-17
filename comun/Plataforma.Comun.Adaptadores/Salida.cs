using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace Plataforma.Comun.Adaptadores;

/// <summary>Configuración de la salida, sección "Salida".</summary>
public sealed class OpcionesSalida
{
    public const string Seccion = "Salida";

    /// <summary>Espera entre dos entregas fallidas mientras el circuito sigue cerrado.</summary>
    public double EsperaReintentoSegundos { get; set; } = 2;

    /// <summary>Con el circuito abierto y el consumo en pausa, cada cuánto se sondea el sistema externo.</summary>
    public double IntervaloSondeoSegundos { get; set; } = 5;

    public static OpcionesSalida Leer(IConfiguration configuracion)
    {
        OpcionesSalida opciones = new();
        configuracion.GetSection(Seccion).Bind(opciones);
        return opciones;
    }
}

/// <summary>Llamada barata e idempotente que dice si el sistema externo responde. Pasa por la misma resiliencia.</summary>
public interface ISondaSistemaExterno
{
    Task SondearAsync(CancellationToken ct);
}

/// <summary>
/// Adaptador de entrada de eventos de la salida (AD-36): el consumidor idempotente de comun le pasa cada mensaje de la
/// cola del adaptador y él lo entrega al sistema externo.
///
/// <para>Si el sistema no responde, <b>no lanza</b>: un error transitorio haría que el consumidor reencolara el mensaje y,
/// al llegar al límite de entregas, lo apartara a la cola de mensajes muertos, aunque el mensaje esté bien. En su lugar
/// espera y reintenta. Cuando el circuito se abre, <see cref="CompuertaConsumo{TContexto}"/> detiene el consumidor: su
/// cancelación interrumpe esta espera y el mensaje vuelve a la cola sin confirmarse.</para>
///
/// <para>Si el sistema rechaza el contenido (4xx) el error es permanente: se informa como <see cref="EventoInvalidoException"/>
/// y el consumidor lo aparta a la cola de mensajes muertos.</para>
/// </summary>
public sealed class ManejadorSalida : IManejadorEvento
{
    private readonly ISalidaAdaptador _adaptador;
    private readonly MonitorCircuito _monitor;
    private readonly OpcionesSalida _opciones;
    private readonly ILogger<ManejadorSalida> _log;

    public ManejadorSalida(ISalidaAdaptador adaptador, MonitorCircuito monitor, OpcionesSalida opciones, ILogger<ManejadorSalida> log)
    {
        _adaptador = adaptador;
        _monitor = monitor;
        _opciones = opciones;
        _log = log;
    }

    public async Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        TimeSpan espera = TimeSpan.FromSeconds(Math.Max(0.1, _opciones.EsperaReintentoSegundos));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ResultadoEntrega resultado = await _adaptador.Entregar(sobre, ct);
                _log.LogInformation("{Tipo} {Clave} a {Sistema}: {Resultado}.", sobre.Tipo, sobre.ClaveIdempotencia,
                    _adaptador.SistemaExterno, resultado);
                return;
            }
            catch (EntregaRechazadaException e)
            {
                throw new EventoInvalidoException($"{_adaptador.SistemaExterno} rechazó {sobre.Tipo} {sobre.ClaveIdempotencia}: {e.Message}", e);
            }
            catch (Exception e) when (Indisponibilidad.Es(e, ct))
            {
                _log.LogWarning("{Sistema} no responde ({Error}); {Tipo} {Clave} sigue pendiente.", _adaptador.SistemaExterno,
                    e.GetType().Name, sobre.Tipo, sobre.ClaveIdempotencia);
                // Con el circuito abierto la compuerta detiene el consumidor y su cancelación corta esta espera. Con el
                // circuito cerrado (fallos sueltos que aún no lo abren) se reintenta en un momento.
                await _monitor.EsperarCambioAsync(espera, ct);
            }
        }
    }
}

/// <summary>
/// Compuerta del consumo de la cola de salida (AD-36, Cuadro 5: "acumula los mensajes dirigidos a un sistema externo no
/// disponible y los entrega cuando el circuito se cierra"). Arranca el consumidor idempotente de comun con el circuito
/// cerrado y lo detiene cuando se abre; mientras está abierto sondea el sistema externo y, al cerrarse, lo arranca otra vez.
///
/// <para>Por qué detener el consumidor y no retener el mensaje: el consumidor de comun cuenta las entregas de cada mensaje
/// (x-acquired-count) y lo aparta a la cola de mensajes muertos al llegar al límite (5). Un manejador que lanzara con el
/// circuito abierto recibiría el mismo mensaje una y otra vez y lo mandaría a la DLQ en segundos. Retenerlo sin
/// confirmar tampoco sirve en una caída larga (QA-01 c: 72 h): RabbitMQ cierra el canal al vencer su consumer_timeout
/// (30 min por omisión) y KEDA, que mide los mensajes listos, vería la cola vacía y bajaría el adaptador a cero. Con la
/// compuerta, al abrirse el circuito solo el mensaje en curso (prefetch 1) vuelve a la cola, una vez por caída; los demás
/// nunca se toman y quedan listos en la cola, donde KEDA los cuenta.</para>
/// </summary>
public sealed class CompuertaConsumo<TContexto> : BackgroundService where TContexto : DbContext
{
    private readonly IServiceProvider _servicios;
    private readonly MonitorCircuito _monitor;
    private readonly OpcionesSalida _opciones;
    private readonly ILogger<CompuertaConsumo<TContexto>> _log;
    private ConsumidorIdempotente<TContexto>? _consumidor;

    public CompuertaConsumo(IServiceProvider servicios, MonitorCircuito monitor, OpcionesSalida opciones,
        ILogger<CompuertaConsumo<TContexto>> log)
    {
        _servicios = servicios;
        _monitor = monitor;
        _opciones = opciones;
        _log = log;
    }

    /// <summary>El consumidor está tomando mensajes de la cola.</summary>
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
                    _consumidor = ActivatorUtilities.CreateInstance<ConsumidorIdempotente<TContexto>>(_servicios);
                    await _consumidor.StartAsync(ct);
                    _log.LogInformation("{Sistema} responde: se consume la cola de salida.", _monitor.SistemaExterno);
                    continue;
                }
                if (abierto && _consumidor is not null)
                {
                    await DetenerAsync();
                    _log.LogWarning("{Sistema} no responde: se pausa el consumo; los mensajes esperan en la cola de salida.",
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
            // Si ya pasó la duración de la apertura, esta llamada es la prueba del cortacircuitos: al responder, lo cierra.
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
        ConsumidorIdempotente<TContexto>? consumidor = _consumidor;
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
            _log.LogWarning(e, "El consumidor de la cola de salida no se detuvo limpiamente.");
        }
        finally
        {
            consumidor.Dispose();
        }
    }
}

public static class RegistroAdaptadores
{
    /// <summary>
    /// Salida de un adaptador: consumidor idempotente de su cola (Bus:Cola) gobernado por la compuerta, y el manejador que
    /// entrega con <typeparamref name="TSalida"/>. Requiere <see cref="AgregarEstadoIntegracion{TContexto}"/> y el
    /// cliente HTTP del sistema externo con <see cref="RegistroResiliencia.AgregarResilienciaAdaptador"/>.
    /// </summary>
    public static IServiceCollection AgregarSalidaAdaptador<TContexto, TSalida>(this IServiceCollection servicios, IConfiguration configuracion)
        where TContexto : DbContext
        where TSalida : class, ISalidaAdaptador, ISondaSistemaExterno
    {
        servicios.AddSingleton(OpcionesSalida.Leer(configuracion));
        servicios.AddScoped<ISalidaAdaptador>(sp => sp.GetRequiredService<TSalida>());
        servicios.AddScoped<ISondaSistemaExterno>(sp => sp.GetRequiredService<TSalida>());
        servicios.AddScoped<IManejadorEvento, ManejadorSalida>();
        servicios.AddSingleton<CompuertaConsumo<TContexto>>();
        servicios.AddHostedService(sp => sp.GetRequiredService<CompuertaConsumo<TContexto>>());
        return servicios;
    }

    /// <summary>Entrada de un adaptador: consulta periódica desde la marca de avance, con <typeparamref name="TEntrada"/>.</summary>
    public static IServiceCollection AgregarEntradaAdaptador<TContexto, TExterno, TEntrada>(this IServiceCollection servicios,
        IConfiguration configuracion)
        where TContexto : DbContext
        where TEntrada : class, IEntradaAdaptador<TExterno>
    {
        servicios.AddSingleton(OpcionesEntrada.Leer(configuracion));
        servicios.AddScoped<IEntradaAdaptador<TExterno>>(sp => sp.GetRequiredService<TEntrada>());
        servicios.AddScoped<IAlmacenEntrada, AlmacenEntradaBandeja<TContexto>>();
        servicios.AddSingleton<ServicioEntradaPeriodica<TExterno>>();
        servicios.AddHostedService(sp => sp.GetRequiredService<ServicioEntradaPeriodica<TExterno>>());
        return servicios;
    }

    /// <summary>
    /// Estado de la integración (E-15): el <see cref="MonitorCircuito"/> del sistema externo y el publicador de EV-23 por
    /// la bandeja de salida de <typeparamref name="TContexto"/>. <paramref name="colaPendientes"/> es la cola cuyos mensajes
    /// se informan en mensajesEnCola.
    /// </summary>
    public static IServiceCollection AgregarEstadoIntegracion<TContexto>(this IServiceCollection servicios, string sistemaExterno,
        string? colaPendientes) where TContexto : DbContext
    {
        servicios.AddSingleton(new MonitorCircuito(sistemaExterno));
        servicios.AddSingleton<IContadorMensajesEnCola>(sp => colaPendientes is null
            ? new ContadorSinCola()
            : ActivatorUtilities.CreateInstance<ContadorColaBus>(sp, colaPendientes));
        servicios.AddHostedService<PublicadorEstadoIntegracion<TContexto>>();
        return servicios;
    }
}
