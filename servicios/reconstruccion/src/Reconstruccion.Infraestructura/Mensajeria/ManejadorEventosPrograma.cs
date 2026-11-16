using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Dominio;

namespace Reconstruccion.Infraestructura.Mensajeria;

/// <summary>
/// Adaptador de entrada de la Consulta del Programa (AD-42): traduce EV-18, EV-19 y EV-20 al caso de uso que
/// actualiza el modelo de lectura. Los tres traen el estado resultante del proyecto con su versión, así que el orden
/// de llegada no importa. EV-23 (estado de las integraciones) llega a la misma cola y se proyecta cuando existan los
/// adaptadores (paso de integraciones); por ahora se ignora.
/// </summary>
public sealed class ManejadorEventosPrograma : IManejadorEvento
{
    private readonly ActualizarProyeccionPrograma _actualizar;
    private readonly ILogger<ManejadorEventosPrograma> _log;

    public ManejadorEventosPrograma(ActualizarProyeccionPrograma actualizar, ILogger<ManejadorEventosPrograma> log)
    {
        _actualizar = actualizar;
        _log = log;
    }

    public Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        (Guid idProyecto, ResumenProyecto resumen) = sobre.Tipo switch
        {
            "EV-18" => Desde(sobre.LeerDatos<ProyectoCreadoV1>()),
            "EV-19" => Desde(sobre.LeerDatos<PresupuestoCambiadoV1>()),
            "EV-20" => Desde(sobre.LeerDatos<EstadoOAvanceCambiadoV1>()),
            _ => (Guid.Empty, null!),
        };
        if (idProyecto == Guid.Empty)
        {
            _log.LogDebug("La Consulta del Programa no proyecta {Tipo}; se ignora.", sobre.Tipo);
            return Task.CompletedTask;
        }
        FilaPrograma fila = new(idProyecto, resumen.IdDesastre, resumen.Nombre, MapeoCanonico.AlDominio(resumen.Estado),
            resumen.Presupuesto ?? 0, resumen.CostoEjecutado, resumen.Avance, resumen.Version, sobre.InstanteEmision);
        return _actualizar.EjecutarAsync(fila, ct);
    }

    private static (Guid, ResumenProyecto) Desde(ProyectoCreadoV1 d) => (d.IdProyecto, d.Proyecto);
    private static (Guid, ResumenProyecto) Desde(PresupuestoCambiadoV1 d) => (d.IdProyecto, d.Proyecto);
    private static (Guid, ResumenProyecto) Desde(EstadoOAvanceCambiadoV1 d) => (d.IdProyecto, d.Proyecto);
}
