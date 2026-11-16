using EvaluacionDanos.Aplicacion.CasosDeUso;
using EvaluacionDanos.Aplicacion.Puertos;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;

namespace EvaluacionDanos.Infraestructura.Mensajeria;

/// <summary>
/// Adaptador de entrada de la cola <c>q.evaluacion-danos</c>: traduce el sobre canónico al caso de uso que mantiene
/// las copias locales. EV-03, EV-05, EV-06 y EV-27 alimentan la copia de desastres (E-29, E-25); EV-21, la capa de
/// municipios (E-11). No guarda por su cuenta: el consumidor confirma el efecto en la misma transacción que el
/// registro de idempotencia, así un evento repetido no se aplica dos veces (AD-02).
/// </summary>
public sealed class ManejadorEventosEntrantes(ActualizarCopiaDeDesastres desastres, CargarCapaGeografica capa,
    ILogger<ManejadorEventosEntrantes> log) : IManejadorEvento
{
    public Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        switch (sobre.Tipo)
        {
            case "EV-03":
            {
                var d = sobre.LeerDatos<DesastreCreadoV1>();
                return desastres.DesastreCreadoAsync(d.IdDesastre, d.Nombre, d.Tipo, MapeoCanonico.AlDominio(d.Estado),
                    d.VersionDesastre, ct);
            }
            case "EV-05":
            {
                var d = sobre.LeerDatos<ZonaActualizadaV1>();
                return desastres.ZonaActualizadaAsync(d.IdDesastre, d.ZonaVersion, d.CodigosMunicipio, ct);
            }
            case "EV-06":
            {
                var d = sobre.LeerDatos<EstadoDesastreCambiadoV1>();
                return desastres.EstadoCambiadoAsync(d.IdDesastre, MapeoCanonico.AlDominio(d.EstadoNuevo), d.VersionDesastre, ct);
            }
            case "EV-27":
            {
                var d = sobre.LeerDatos<OrganismosDesastreCambiadosV1>();
                return desastres.OrganismosCambiadosAsync(d.IdDesastre, d.OrganismoResponsable,
                    d.Participantes.Select(p => p.Organismo).ToList(), d.Version, ct);
            }
            case "EV-21":
            {
                var d = sobre.LeerDatos<CapaGeograficaActualizadaV1>();
                var divisiones = d.Divisiones
                    .Select(x => new DivisionRecibida(x.Codigo, x.Nombre, x.CodigoPadre, x.Geometria.GetRawText())).ToList();
                return capa.ParteRecibidaAsync(d.Capa.ToString(), d.VersionCapa, d.Parte, d.Total, divisiones, ct);
            }
            default:
                log.LogDebug("Evaluación de Daños no usa {Tipo}; se ignora.", sobre.Tipo);
                return Task.CompletedTask;
        }
    }
}
