using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;

namespace Plataforma.Comun.Adaptadores;

/// <summary>Configuración de la entrada por consulta periódica, sección "Entrada".</summary>
public sealed class OpcionesEntrada
{
    public const string Seccion = "Entrada";

    /// <summary>Nombre del flujo en la tabla de marcas de avance.</summary>
    public string Flujo { get; set; } = "cambios";

    /// <summary>Intervalo de la consulta periódica (DA-11 propone 30 s: es la latencia de entrada aceptada).</summary>
    public double IntervaloSegundos { get; set; } = 30;

    /// <summary>Páginas por ronda, para que una ronda tras una caída larga no se alargue sin límite.</summary>
    public int MaximoPaginasPorRonda { get; set; } = 20;

    public static OpcionesEntrada Leer(IConfiguration configuracion)
    {
        OpcionesEntrada opciones = new();
        configuracion.GetSection(Seccion).Bind(opciones);
        return opciones;
    }
}

public sealed record ResultadoRonda(int Paginas, int Cambios, int Eventos, bool Conflicto);

/// <summary>
/// Una ronda de la entrada (AD-14): desde la marca de avance pide los cambios al sistema externo, los traduce al evento
/// canónico y confirma cada página con la marca en una transacción (<see cref="IAlmacenEntrada.ConfirmarAsync"/>).
/// Si el proceso se detiene a mitad, la siguiente ronda empieza en la última página confirmada: la página que no se
/// confirmó se vuelve a pedir y ninguna se publica dos veces.
/// </summary>
public sealed class CicloEntrada<TExterno>
{
    private readonly IEntradaAdaptador<TExterno> _adaptador;
    private readonly IAlmacenEntrada _almacen;
    private readonly OpcionesEntrada _opciones;

    public CicloEntrada(IEntradaAdaptador<TExterno> adaptador, IAlmacenEntrada almacen, OpcionesEntrada opciones)
    {
        _adaptador = adaptador;
        _almacen = almacen;
        _opciones = opciones;
    }

    public async Task<ResultadoRonda> EjecutarAsync(CancellationToken ct)
    {
        int paginas = 0, cambios = 0, eventos = 0;
        string? marca = await _almacen.LeerMarcaAsync(_opciones.Flujo, ct);
        while (paginas < Math.Max(1, _opciones.MaximoPaginasPorRonda))
        {
            LoteExterno<TExterno> lote = await _adaptador.RecuperarDesde(marca, ct);
            paginas++;
            if (lote.Cambios.Count == 0 || lote.MarcaSiguiente is null || lote.MarcaSiguiente == marca)
            {
                break;
            }
            List<Sobre> sobres = lote.Cambios.Select(_adaptador.Traducir).OfType<Sobre>().ToList();
            if (!await _almacen.ConfirmarAsync(_opciones.Flujo, marca, lote.MarcaSiguiente, lote.Cambios.Count, sobres, ct))
            {
                return new ResultadoRonda(paginas, cambios, eventos, true);
            }
            cambios += lote.Cambios.Count;
            eventos += sobres.Count;
            marca = lote.MarcaSiguiente;
            if (!lote.HayMas)
            {
                break;
            }
        }
        return new ResultadoRonda(paginas, cambios, eventos, false);
    }
}

/// <summary>
/// Consulta periódica del sistema externo (DA-11). Cada ronda corre en su propio ámbito. Si el sistema no responde, la
/// ronda termina sin avanzar la marca y el cortacircuitos registra el fallo. Al cerrarse el circuito no espera al
/// siguiente intervalo: pide enseguida el rango pendiente desde la marca (Cuadro 5, "Marca de avance").
/// </summary>
public sealed class ServicioEntradaPeriodica<TExterno> : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly MonitorCircuito _monitor;
    private readonly OpcionesEntrada _opciones;
    private readonly ILogger<ServicioEntradaPeriodica<TExterno>> _log;

    public ServicioEntradaPeriodica(IServiceScopeFactory ambitos, MonitorCircuito monitor, OpcionesEntrada opciones,
        ILogger<ServicioEntradaPeriodica<TExterno>> log)
    {
        _ambitos = ambitos;
        _monitor = monitor;
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        TimeSpan intervalo = TimeSpan.FromSeconds(Math.Max(1, _opciones.IntervaloSegundos));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RondaAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (Indisponibilidad.Es(e, ct))
            {
                _log.LogWarning("{Sistema} no responde ({Error}); la marca de avance no se mueve y se reintenta.",
                    _monitor.SistemaExterno, e.GetType().Name);
            }
            catch (Exception e)
            {
                // La base o el bus no responden, o un cambio no se pudo traducir: se reintenta en la siguiente ronda.
                _log.LogError(e, "La ronda de entrada de {Sistema} falló; se reintenta.", _monitor.SistemaExterno);
            }
            try
            {
                await _monitor.EsperarCambioAsync(intervalo, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<ResultadoRonda> RondaAsync(CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        CicloEntrada<TExterno> ciclo = new(ambito.ServiceProvider.GetRequiredService<IEntradaAdaptador<TExterno>>(),
            ambito.ServiceProvider.GetRequiredService<IAlmacenEntrada>(), _opciones);
        ResultadoRonda resultado = await ciclo.EjecutarAsync(ct);
        if (resultado.Cambios > 0 || resultado.Conflicto)
        {
            _log.LogInformation("Entrada de {Sistema}: {Cambios} cambios, {Eventos} eventos en la bandeja ({Paginas} páginas){Conflicto}.",
                _monitor.SistemaExterno, resultado.Cambios, resultado.Eventos, resultado.Paginas,
                resultado.Conflicto ? "; otra instancia confirmó la misma página" : "");
        }
        return resultado;
    }
}
