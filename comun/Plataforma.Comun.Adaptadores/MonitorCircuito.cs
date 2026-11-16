using System.Threading.Channels;
using Plataforma.Comun.Eventos;

namespace Plataforma.Comun.Adaptadores;

/// <summary>Un cambio del estado observable de la integración con un sistema externo (se publica en EV-23).</summary>
public sealed record TransicionCircuito(Guid Id, string SistemaExterno, EstadoIntegracion Anterior, EstadoIntegracion Nuevo,
    DateTimeOffset Instante);

/// <summary>
/// Estado observable del cortacircuitos de un adaptador (AD-05, AD-37) y la señal con la que la salida y la entrada
/// reaccionan a él. Lo alimentan las notificaciones del cortacircuitos de Polly (<see cref="RegistroResiliencia"/>).
///
/// Solo se publica la apertura y el cierre (DA-11): la plataforma ve CERRADO o ABIERTO. Los pasos por SEMIABIERTO
/// (cada prueba del cortacircuitos mientras el sistema sigue caído) no se publican: durante una caída larga generarían
/// dos eventos por cada BreakDuration, y la nueva apertura movería el "no responde desde" que la consola muestra
/// (QA-01 b). El instante de la primera apertura se conserva hasta que el circuito se cierra.
/// </summary>
public sealed class MonitorCircuito
{
    private readonly object _candado = new();
    private readonly Channel<TransicionCircuito> _transiciones = Channel.CreateUnbounded<TransicionCircuito>();
    private readonly TimeProvider _reloj;
    private TaskCompletionSource _cambio = NuevaSenal();

    public MonitorCircuito(string sistemaExterno, TimeProvider? reloj = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sistemaExterno);
        SistemaExterno = sistemaExterno;
        _reloj = reloj ?? TimeProvider.System;
    }

    public string SistemaExterno { get; }

    /// <summary>CERRADO o ABIERTO: el estado que se publica.</summary>
    public EstadoIntegracion Estado { get; private set; } = EstadoIntegracion.CERRADO;

    /// <summary>Instante de la apertura vigente, si el circuito está abierto.</summary>
    public DateTimeOffset? AbiertoDesde { get; private set; }

    /// <summary>El cortacircuitos está dejando pasar una llamada de prueba.</summary>
    public bool Semiabierto { get; private set; }

    /// <summary>Transiciones pendientes de publicar en EV-23. Las consume <see cref="PublicadorEstadoIntegracion{TContexto}"/>.</summary>
    public ChannelReader<TransicionCircuito> Transiciones => _transiciones.Reader;

    public void AlAbrirse()
    {
        lock (_candado)
        {
            Semiabierto = false;
            if (Estado == EstadoIntegracion.ABIERTO)
            {
                return; // una prueba fallida vuelve a abrirlo: para la plataforma sigue abierto desde la primera vez
            }
            DateTimeOffset ahora = _reloj.GetUtcNow();
            Cambiar(EstadoIntegracion.ABIERTO, ahora);
            AbiertoDesde = ahora;
        }
    }

    public void AlSemiabrirse()
    {
        lock (_candado)
        {
            Semiabierto = true;
        }
    }

    public void AlCerrarse()
    {
        lock (_candado)
        {
            Semiabierto = false;
            if (Estado == EstadoIntegracion.CERRADO)
            {
                return;
            }
            Cambiar(EstadoIntegracion.CERRADO, _reloj.GetUtcNow());
            AbiertoDesde = null;
        }
    }

    /// <summary>Espera a que el estado observable cambie, o a que pase <paramref name="maximo"/>. No lanza por el plazo.</summary>
    public Task EsperarCambioAsync(TimeSpan maximo, CancellationToken ct) => EsperarCambioAsync(null, maximo, ct);

    /// <summary>
    /// Como <see cref="EsperarCambioAsync(TimeSpan, CancellationToken)"/>, pero vuelve enseguida si el estado ya no es
    /// <paramref name="conocido"/>: así no se pierde un cambio ocurrido entre leer el estado y empezar a esperar.
    /// </summary>
    public async Task EsperarCambioAsync(EstadoIntegracion? conocido, TimeSpan maximo, CancellationToken ct)
    {
        Task senal;
        lock (_candado)
        {
            if (conocido is not null && Estado != conocido)
            {
                return;
            }
            senal = _cambio.Task;
        }
        await Task.WhenAny(senal, Task.Delay(maximo, _reloj, ct));
        ct.ThrowIfCancellationRequested();
    }

    private void Cambiar(EstadoIntegracion nuevo, DateTimeOffset instante)
    {
        TransicionCircuito transicion = new(Guid.NewGuid(), SistemaExterno, Estado, nuevo, instante);
        Estado = nuevo;
        _transiciones.Writer.TryWrite(transicion);
        TaskCompletionSource anterior = _cambio;
        _cambio = NuevaSenal();
        anterior.TrySetResult();
    }

    private static TaskCompletionSource NuevaSenal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
