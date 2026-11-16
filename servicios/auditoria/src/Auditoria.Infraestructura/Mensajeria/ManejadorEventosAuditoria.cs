using System.Text.Json;
using Auditoria.Aplicacion;
using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Dominio;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace Auditoria.Infraestructura.Mensajeria;

/// <summary>
/// Adaptador de entrada de Auditoría (AD-21, DA-17): traduce cada sobre de <c>q.auditoria</c> (enlazada a <c>#</c>)
/// al caso de uso que lo registra. A diferencia de los demás consumidores, no ignora ningún tipo: todo evento de cambio
/// queda en el rastro. No guarda por su cuenta: el consumidor idempotente confirma la entrada y la marca de mensaje
/// procesado en la misma transacción (DA-03).
/// </summary>
public sealed class ManejadorEventosAuditoria : IManejadorEvento
{
    private readonly IRegistrarEventoEntrada _registrar;
    private readonly ILogger<ManejadorEventosAuditoria> _log;

    public ManejadorEventosAuditoria(IRegistrarEventoEntrada registrar, ILogger<ManejadorEventosAuditoria> log)
    {
        _registrar = registrar;
        _log = log;
    }

    public async Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        bool nuevo = await _registrar.EjecutarAsync(Traducir(sobre, _log), ct);
        if (!nuevo)
        {
            _log.LogDebug("El evento {IdEvento} ({Tipo}) ya estaba registrado; se descarta.", sobre.IdEvento, sobre.Tipo);
        }
    }

    public static ComandoRegistrarEvento Traducir(Sobre sobre, ILogger? log = null)
    {
        Interpretacion interpretacion;
        try
        {
            interpretacion = InterpretacionEventos.Interpretar(sobre);
        }
        catch (JsonException e)
        {
            // El sobre ya pasó el esquema del contrato; si aun así sus datos no se pueden leer, el evento se guarda igual
            // con el sobre completo. Perder la entrada sería peor que no interpretarla (QA-13 a).
            log?.LogWarning(e, "No se pudieron interpretar los datos del evento {Tipo} {IdEvento}; se registra sin interpretar.",
                sobre.Tipo, sobre.IdEvento);
            interpretacion = Interpretacion.Desconocida;
        }
        if (interpretacion == Interpretacion.Desconocida)
        {
            log?.LogInformation("Auditoría no interpreta {Tipo} v{Version}; se registra el sobre sin actor ni entidad.",
                sobre.Tipo, sobre.Version);
        }
        return new ComandoRegistrarEvento(
            sobre.IdEvento,
            sobre.Tipo,
            sobre.Nombre,
            sobre.Version,
            sobre.Productor,
            sobre.SecuenciaProductor,
            sobre.InstanteEmision,
            sobre.IdCorrelacion,
            sobre.ClaveIdempotencia,
            interpretacion,
            new ProcedenciaDato(sobre.Procedencia.Fuente, sobre.Procedencia.SistemaOrigen, sobre.Procedencia.Confianza.ToString()),
            sobre.Serializar());
    }
}
