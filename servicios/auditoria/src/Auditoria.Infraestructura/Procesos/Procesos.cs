using Auditoria.Aplicacion;
using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Aplicacion.Puertos;
using Auditoria.Dominio;
using Auditoria.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auditoria.Infraestructura.Procesos;

/// <summary>Configuración del servicio, sección "Auditoria".</summary>
public sealed class OpcionesAuditoria
{
    public const string Seccion = "Auditoria";

    /// <summary>Un hueco se reporta si persiste más que esto (R-02). Por omisión 60 s.</summary>
    public int PlazoHuecoSegundos { get; set; } = 60;
    public int IntervaloRevisionMs { get; set; } = 10_000;
    /// <summary>Meses hacia adelante con partición ya creada (QA-13 e).</summary>
    public int MesesParticionesAdelante { get; set; } = 12;
    public int IntervaloParticionesMinutos { get; set; } = 24 * 60;

    public static OpcionesAuditoria Leer(IConfiguration configuracion)
    {
        OpcionesAuditoria opciones = new();
        configuracion.GetSection(Seccion).Bind(opciones);
        return opciones;
    }

    public OpcionesHuecos Huecos => new(TimeSpan.FromSeconds(PlazoHuecoSegundos));
}

/// <summary>
/// Alerta de un hueco que persistió más del plazo: un log de advertencia con el código HUECO_DETECTADO (DA-17), que la
/// plataforma de logs puede convertir en alarma. El registro durable es la fila de huecos_secuencia.
/// </summary>
public sealed class AvisosHuecosPorLog : IAvisosHuecos
{
    private readonly ILogger<AvisosHuecosPorLog> _log;

    public AvisosHuecosPorLog(ILogger<AvisosHuecosPorLog> log)
    {
        _log = log;
    }

    public void HuecoReportado(HuecoSecuencia hueco, TimeSpan plazo)
    {
        _log.LogWarning("HUECO_DETECTADO: falta el evento {Secuencia} del productor {Productor} desde {DetectadoEn:o} " +
            "(más de {Plazo} s). El rastro de auditoría puede estar incompleto (ATAM R-02).",
            hueco.Secuencia, hueco.Productor, hueco.DetectadoEn, plazo.TotalSeconds);
    }
}

/// <summary>
/// Ejecuta la revisión de secuencias cada cierto tiempo. Con varias réplicas, un candado consultivo de PostgreSQL
/// (pg_try_advisory_xact_lock) deja que solo una revise a la vez; la otra sigue con la siguiente vuelta. El candado se
/// libera solo al terminar la transacción, también si la réplica cae.
/// </summary>
public sealed class ProcesoRevisionHuecos : BackgroundService
{
    /// <summary>Identificador del candado consultivo. Cualquier número fijo sirve; este es exclusivo de Auditoría.</summary>
    public const long Candado = 2_117_002;

    private readonly IServiceScopeFactory _ambitos;
    private readonly OpcionesAuditoria _opciones;
    private readonly ILogger<ProcesoRevisionHuecos> _log;

    public ProcesoRevisionHuecos(IServiceScopeFactory ambitos, OpcionesAuditoria opciones, ILogger<ProcesoRevisionHuecos> log)
    {
        _ambitos = ambitos;
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RevisarUnaVezAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "La revisión de secuencias falló; se reintenta en la siguiente vuelta.");
            }
            await Task.Delay(_opciones.IntervaloRevisionMs, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    /// <summary>Una vuelta. Devuelve nulo si otra réplica tenía el candado.</summary>
    public async Task<ResumenRevision?> RevisarUnaVezAsync(CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        AuditoriaDbContext db = ambito.ServiceProvider.GetRequiredService<AuditoriaDbContext>();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        bool tomado = (await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({Candado}) AS \"Value\"")
            .ToListAsync(ct)).Single();
        if (!tomado)
        {
            await transaccion.RollbackAsync(ct);
            return null;
        }
        ResumenRevision resumen = await ambito.ServiceProvider.GetRequiredService<IRevisarSecuenciasEntrada>().EjecutarAsync(ct);
        await transaccion.CommitAsync(ct);
        if (resumen.Detectados + resumen.Cubiertos + resumen.Reportados > 0)
        {
            _log.LogInformation("Revisión de secuencias: {Detectados} huecos nuevos, {Cubiertos} cubiertos, {Reportados} reportados.",
                resumen.Detectados, resumen.Cubiertos, resumen.Reportados);
        }
        return resumen;
    }
}

/// <summary>
/// Crea por adelantado las particiones mensuales del registro (QA-13 e). El rol del servicio no tiene permisos de DDL:
/// llama a la función <c>asegurar_particiones_registro</c>, que crea solo particiones del registro, solo hacia adelante
/// y con un máximo de meses. Si una partición faltara de todas formas, la partición por omisión recibe el evento: un
/// evento nunca se rechaza por su fecha.
/// </summary>
public sealed class ProcesoParticiones : BackgroundService
{
    private static readonly TimeSpan EsperaTrasFallo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _ambitos;
    private readonly OpcionesAuditoria _opciones;
    private readonly ILogger<ProcesoParticiones> _log;

    public ProcesoParticiones(IServiceScopeFactory ambitos, OpcionesAuditoria opciones, ILogger<ProcesoParticiones> log)
    {
        _ambitos = ambitos;
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan espera = TimeSpan.FromMinutes(_opciones.IntervaloParticionesMinutos);
            try
            {
                int creadas = await AsegurarAsync(ct);
                if (creadas > 0)
                {
                    _log.LogInformation("Se crearon {Creadas} particiones mensuales del registro.", creadas);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "No se pudieron asegurar las particiones del registro; se reintenta.");
                espera = EsperaTrasFallo;
            }
            await Task.Delay(espera, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    public async Task<int> AsegurarAsync(CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        AuditoriaDbContext db = ambito.ServiceProvider.GetRequiredService<AuditoriaDbContext>();
        int meses = _opciones.MesesParticionesAdelante;
        return (await db.Database
            .SqlQuery<int>($"SELECT asegurar_particiones_registro({meses}) AS \"Value\"")
            .ToListAsync(ct)).Single();
    }
}
