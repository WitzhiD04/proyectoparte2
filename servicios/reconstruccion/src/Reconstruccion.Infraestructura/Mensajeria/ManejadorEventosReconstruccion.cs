using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Reconstruccion.Aplicacion.CasosDeUso;
using Dominio = Reconstruccion.Dominio;

namespace Reconstruccion.Infraestructura.Mensajeria;

/// <summary>
/// Adaptador de entrada de <c>q.reconstruccion</c> (DA-01, E-01): traduce los eventos de los otros módulos a los casos de
/// uso. EV-03 y EV-06 (desastres), EV-08 (evaluaciones validadas) y EV-21 (nombres de municipios) alimentan las copias
/// locales; EV-24 registra en el proyecto lo que reporta SE-7. Corre en la transacción del consumidor idempotente.
/// </summary>
public sealed class ManejadorEventosReconstruccion : IManejadorEvento
{
    /// <summary>Procedencia con la que quedan los reportes del sistema de proyectos externo (E-04, Diseño RC).</summary>
    public const string SistemaSe7 = "SE-7";

    private readonly ActualizarCopiasLocales _copias;
    private readonly RegistrarReporteExterno _reportes;
    private readonly ILogger<ManejadorEventosReconstruccion> _log;

    public ManejadorEventosReconstruccion(ActualizarCopiasLocales copias, RegistrarReporteExterno reportes,
        ILogger<ManejadorEventosReconstruccion> log)
    {
        _copias = copias;
        _reportes = reportes;
        _log = log;
    }

    public async Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        DateTimeOffset t = sobre.InstanteEmision;
        switch (sobre.Tipo)
        {
            case "EV-03":
                DesastreCreadoV1 d = sobre.LeerDatos<DesastreCreadoV1>();
                await _copias.AplicarAsync(new DesastreRecibido(d.IdDesastre, d.Nombre, d.Tipo, d.Estado.ToString(), d.VersionDesastre, t), ct);
                break;
            case "EV-06":
                EstadoDesastreCambiadoV1 e = sobre.LeerDatos<EstadoDesastreCambiadoV1>();
                await _copias.AplicarAsync(new EstadoDesastreRecibido(e.IdDesastre, e.EstadoNuevo.ToString(), e.VersionDesastre, t), ct);
                break;
            case "EV-08":
                EvaluacionValidadaV1 v = sobre.LeerDatos<EvaluacionValidadaV1>();
                await _copias.AplicarAsync(new EvaluacionRecibida(v.IdEvaluacion, v.IdDesastre, v.IdActivo, v.ReferenciaActivo, v.TipoActivo,
                    Nivel(v.NivelDano), v.CodigoMunicipio, v.EstadoValidacion.ToString(), v.Version, t), ct);
                break;
            case "EV-21":
                CapaGeograficaActualizadaV1 capa = sobre.LeerDatos<CapaGeograficaActualizadaV1>();
                if (capa.Capa == CapaGeografica.MUNICIPIOS)
                {
                    await _copias.AplicarAsync(capa.Divisiones.Select(x => new MunicipioRecibido(x.Codigo, x.Nombre, x.CodigoPadre)).ToList(),
                        capa.VersionCapa, ct);
                }
                break;
            case "EV-24":
                ReporteExternoProyectoV1 r = sobre.LeerDatos<ReporteExternoProyectoV1>();
                ReporteExternoRecibido reporte = new(r.IdProyecto, SistemaSe7,
                    r.Tipo == TipoReporteExterno.AVANCE ? r.Avance : null,
                    r.Tipo == TipoReporteExterno.CONTRATISTA ? r.Contratista : null, r.InstanteExterno);
                (ResultadoReporteExterno resultado, string? motivo) = await _reportes.EjecutarAsync(reporte, ct);
                if (resultado != ResultadoReporteExterno.Aplicado)
                {
                    // No se reintenta: un reporte que el proyecto no admite no lo admitirá después. Queda en el log y en
                    // Auditoría (que recibe todos los eventos), con la clave de origen para rastrearlo en SE-7.
                    _log.LogWarning("Reporte de SE-7 {Clave} sobre el proyecto {Proyecto} no aplicado ({Resultado}): {Motivo}",
                        r.ClaveOrigen, r.IdProyecto, resultado, motivo);
                }
                break;
            default:
                _log.LogDebug("Reconstrucción no consume {Tipo}; se ignora.", sobre.Tipo);
                break;
        }
    }

    private static Dominio.NivelDano Nivel(NivelDano nivel) => nivel switch
    {
        NivelDano.SIN_DANO => Dominio.NivelDano.SinDano,
        NivelDano.LEVE => Dominio.NivelDano.Leve,
        NivelDano.MODERADO => Dominio.NivelDano.Moderado,
        NivelDano.GRAVE => Dominio.NivelDano.Grave,
        NivelDano.DESTRUCCION_TOTAL => Dominio.NivelDano.DestruccionTotal,
        _ => throw new EventoInvalidoException($"Nivel de daño desconocido: {nivel}."),
    };
}
