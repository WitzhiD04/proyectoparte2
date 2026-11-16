using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Canon = Plataforma.Comun.Eventos;

namespace ConsultaDanos.Infraestructura;

/// <summary>
/// Adaptador de entrada de q.consulta-danos: traduce cada evento del contrato al hecho del modelo de lectura. El formato
/// del bus (sobre, enumeraciones canónicas) termina aquí. No guarda por su cuenta: el consumidor confirma la transacción
/// junto con el registro de idempotencia.
/// </summary>
public sealed class ManejadorEventosConsultaDanos : IManejadorEvento
{
    private readonly ActualizarProyecciones _actualizar;
    private readonly ILogger<ManejadorEventosConsultaDanos> _log;

    public ManejadorEventosConsultaDanos(ActualizarProyecciones actualizar, ILogger<ManejadorEventosConsultaDanos> log)
    {
        _actualizar = actualizar;
        _log = log;
    }

    public Task ManejarAsync(Canon.Sobre sobre, CancellationToken ct)
    {
        DateTimeOffset t = sobre.InstanteEmision;
        switch (sobre.Tipo)
        {
            case "EV-08":
            {
                EvaluacionValidadaV1 v = sobre.LeerDatos<EvaluacionValidadaV1>();
                ContenidoActivo copia = new(v.IdEvaluacion, v.IdDesastre, v.TipoActivo, Nivel(v.NivelDano), v.CodigoMunicipio,
                    v.Ubicacion.Lat, v.Ubicacion.Lon, null, EstadoTras(v.EstadoValidacion), v.Version);
                return _actualizar.AplicarAsync(new ValidacionCambiada(v.IdActivo, copia, Enum.Parse<EstadoValidacion>(v.EstadoValidacion.ToString()),
                    v.Version, v.ValidadoPor, v.InstanteValidacion, t), ct);
            }
            case "EV-09":
            {
                EvaluacionSincronizadaV1 s = sobre.LeerDatos<EvaluacionSincronizadaV1>();
                ContenidoActivo contenido = new(s.IdEvaluacion, s.IdDesastre, s.TipoActivo, Nivel(s.NivelDano), s.CodigoMunicipio,
                    s.Ubicacion.Lat, s.Ubicacion.Lon, s.PrecisionM, s.EstadoEvaluacion.ToString(), s.VersionEvaluacion);
                // En conflicto, EV-09 puede traer el contenido entrante sin aplicar con la versión del activo que ya estaba.
                OrigenContenido origen = s.EnConflicto ? OrigenContenido.SincronizacionEnConflicto : OrigenContenido.Evaluacion;
                return _actualizar.AplicarAsync(new EvaluacionRegistrada(s.IdActivo, contenido, s.VersionActivo, origen,
                    OrigenHistorial.Sincronizacion, s.EnConflicto, s.IdDispositivo, t), ct);
            }
            case "EV-26":
            {
                EvaluacionActualizadaV1 a = sobre.LeerDatos<EvaluacionActualizadaV1>();
                string motivo = a.Motivo == Canon.MotivoActualizacion.ASOCIACION ? OrigenHistorial.Asociacion : OrigenHistorial.Actualizacion;
                return _actualizar.AplicarAsync(new EvaluacionRegistrada(a.Evaluacion.IdActivo, Contenido(a.Evaluacion),
                    a.Evaluacion.VersionActivo, OrigenContenido.Evaluacion, motivo, false, a.Autor, t), ct);
            }
            case "EV-10":
            {
                ConflictoDetectadoV1 c = sobre.LeerDatos<ConflictoDetectadoV1>();
                Guid? entrante = c.Versiones.FirstOrDefault(v => !v.Vigente)?.IdEvaluacion;
                return _actualizar.AplicarAsync(new ConflictoDetectado(c.IdConflicto, c.IdActivo, c.TipoConflicto.ToString(), entrante, t), ct);
            }
            case "EV-22":
            {
                ConflictoResueltoV1 r = sobre.LeerDatos<ConflictoResueltoV1>();
                return _actualizar.AplicarAsync(new ConflictoResuelto(r.IdConflicto, r.Decision.ToString(), r.DecididoPor, r.Justificacion,
                    r.Evaluacion.IdActivo, Contenido(r.Evaluacion), r.Evaluacion.VersionActivo, t), ct);
            }
            case "EV-21":
            {
                CapaGeograficaActualizadaV1 capa = sobre.LeerDatos<CapaGeograficaActualizadaV1>();
                if (capa.Capa != Canon.CapaGeografica.MUNICIPIOS)
                {
                    return Task.CompletedTask; // la zona del mapa es el municipio (P-04)
                }
                return _actualizar.AplicarAsync(new CapaRecibida(capa.Divisiones.Select(d => new DivisionLeida(d.Codigo, d.Nombre, d.CodigoPadre,
                    d.Geometria.GetRawText())).ToList(), capa.VersionCapa, t), ct);
            }
            case "EV-25":
            {
                TipoActivoActualizadoV1 tipo = sobre.LeerDatos<TipoActivoActualizadoV1>();
                return _actualizar.AplicarAsync(new TipoActivoActualizado(tipo.Codigo, tipo.Nombre, tipo.Version, t), ct);
            }
            case "EV-23":
            {
                EstadoIntegracionCambiadoV1 i = sobre.LeerDatos<EstadoIntegracionCambiadoV1>();
                return _actualizar.AplicarAsync(new EstadoIntegracion(i.SistemaExterno, i.EstadoAnterior.ToString(), i.EstadoNuevo.ToString(),
                    i.Instante, i.MensajesEnCola), ct);
            }
            default:
                _log.LogDebug("Consulta de Daños no proyecta {Tipo}; se ignora.", sobre.Tipo);
                return Task.CompletedTask;
        }
    }

    private static NivelDano Nivel(Canon.NivelDano nivel) => Enum.Parse<NivelDano>(nivel.ToString());

    private static ContenidoActivo Contenido(Canon.Datos.InstantaneaEvaluacion e) => new(e.IdEvaluacion, e.IdDesastre, e.TipoActivo,
        Nivel(e.NivelDano), e.CodigoMunicipio, e.Ubicacion.Lat, e.Ubicacion.Lon, e.PrecisionM, e.EstadoEvaluacion.ToString(), e.VersionEvaluacion);

    /// <summary>El estado de la evaluación después de validarla, igual que en Evaluación de Daños (pendiente vuelve a registrada).</summary>
    private static string EstadoTras(Canon.EstadoValidacion estado) => estado switch
    {
        Canon.EstadoValidacion.VALIDADA => nameof(Canon.EstadoEvaluacion.VALIDADA),
        Canon.EstadoValidacion.RECHAZADA => nameof(Canon.EstadoEvaluacion.RECHAZADA),
        _ => nameof(Canon.EstadoEvaluacion.REGISTRADA),
    };
}
