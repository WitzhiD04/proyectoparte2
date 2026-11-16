using System.Globalization;
using Auditoria.Dominio;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;

namespace Auditoria.Infraestructura.Mensajeria;

/// <summary>
/// Lee de los datos de cada evento del catálogo quién, sobre qué, qué hizo, con qué resultado y el valor anterior y el
/// nuevo (enunciado, sección Trazabilidad; QA-13; QA-15 a). Es la única parte de Auditoría que conoce los datos de
/// cada tipo; el resto solo conoce el sobre.
/// <para>
/// Un tipo o una versión que no aparece aquí se registra igual, con actor, entidad, acción y resultado nulos: un evento
/// válido nunca se rechaza por ser nuevo (QA-09, regla de evolución aditiva de R-03). Basta con agregar su caso.
/// </para>
/// Los actores que no son personas se escriben "sistema:&lt;productor&gt;" o "dispositivo:&lt;id&gt;", para que no se
/// confundan con un usuario.
/// </summary>
public static class InterpretacionEventos
{
    public const string Desastre = "desastre";
    public const string Evaluacion = "evaluacion";
    public const string Conflicto = "conflicto";
    public const string Proyecto = "proyecto";
    public const string TipoActivo = "tipo-activo";
    public const string CapaGeografica = "capa-geografica";
    public const string Integracion = "integracion";
    public const string Notificacion = "notificacion";

    private const ResultadoOperacion Exitoso = ResultadoOperacion.Exitoso;

    public static Interpretacion Interpretar(Sobre sobre)
    {
        // Solo la versión 1 tiene esquema hoy. Una versión nueva de un tipo conocido convive con la anterior (R-03) y
        // puede cambiar sus campos, así que se trata como desconocida hasta agregar su caso.
        if (sobre.Version != 1)
        {
            return Interpretacion.Desconocida;
        }
        string sistema = "sistema:" + sobre.Productor;
        return sobre.Tipo switch
        {
            "EV-03" => De(sobre.LeerDatos<DesastreCreadoV1>(), d =>
                // SRS Cuadro 7: el organismo que registra el desastre es quien ejecuta la operación.
                new Interpretacion(d.OrganismoRegistra, Desastre, Texto(d.IdDesastre), "CREACION", Exitoso, null, d.Estado.ToString())),
            "EV-04" => De(sobre.LeerDatos<SeveridadCambiadaV1>(), d =>
                new Interpretacion(d.Autor, Desastre, Texto(d.IdDesastre), "CAMBIO_SEVERIDAD", Exitoso,
                    d.SeveridadAnterior.ToString(), d.SeveridadNueva.ToString())),
            "EV-05" => De(sobre.LeerDatos<ZonaActualizadaV1>(), d =>
                new Interpretacion(d.Autor, Desastre, Texto(d.IdDesastre), "CAMBIO_ZONA", Exitoso, null,
                    $"zona v{d.ZonaVersion}: {string.Join(",", d.CodigosMunicipio)}")),
            "EV-06" => De(sobre.LeerDatos<EstadoDesastreCambiadoV1>(), d =>
                new Interpretacion(d.Autor, Desastre, Texto(d.IdDesastre), "CAMBIO_ESTADO", Exitoso,
                    d.EstadoAnterior.ToString(), d.EstadoNuevo.ToString())),
            "EV-27" => De(sobre.LeerDatos<OrganismosDesastreCambiadosV1>(), d =>
                new Interpretacion(d.Autor, Desastre, Texto(d.IdDesastre), "CAMBIO_ORGANISMOS", Exitoso, null,
                    d.OrganismoResponsable + ": " + string.Join(",", d.Participantes.Select(p => p.Organismo)))),
            "EV-08" => De(sobre.LeerDatos<EvaluacionValidadaV1>(), d =>
                new Interpretacion(d.ValidadoPor, Evaluacion, Texto(d.IdEvaluacion), "VALIDACION", Exitoso, null,
                    d.EstadoValidacion.ToString())),
            "EV-09" => De(sobre.LeerDatos<EvaluacionSincronizadaV1>(), d =>
                // EV-09 no trae al evaluador, solo el dispositivo que sincronizó.
                new Interpretacion("dispositivo:" + d.IdDispositivo, Evaluacion, Texto(d.IdEvaluacion), "SINCRONIZACION",
                    Exitoso, null, d.EstadoEvaluacion.ToString())),
            "EV-10" => De(sobre.LeerDatos<ConflictoDetectadoV1>(), d =>
                new Interpretacion(sistema, Conflicto, Texto(d.IdConflicto), "DETECCION_CONFLICTO", Exitoso, null,
                    d.TipoConflicto.ToString())),
            "EV-22" => De(sobre.LeerDatos<ConflictoResueltoV1>(), d =>
                new Interpretacion(d.DecididoPor, Conflicto, Texto(d.IdConflicto), "RESOLUCION_CONFLICTO", Exitoso,
                    null, d.Decision.ToString())),
            "EV-26" => De(sobre.LeerDatos<EvaluacionActualizadaV1>(), d =>
                new Interpretacion(d.Autor, Evaluacion, Texto(d.IdEvaluacion), d.Motivo.ToString(), Exitoso, null,
                    d.Evaluacion.EstadoEvaluacion.ToString())),
            "EV-25" => De(sobre.LeerDatos<TipoActivoActualizadoV1>(), d =>
                new Interpretacion(sistema, TipoActivo, d.Codigo, "ACTUALIZACION_CATALOGO", Exitoso, null, "v" + d.Version)),
            // EV-18 trae el autor desde E-33; un mensaje anterior a E-33 queda con el actor nulo.
            "EV-18" => De(sobre.LeerDatos<ProyectoCreadoV1>(), d =>
                new Interpretacion(d.Autor, Proyecto, Texto(d.IdProyecto), "CREACION", Exitoso, null, d.Proyecto.Estado.ToString())),
            "EV-19" => De(sobre.LeerDatos<PresupuestoCambiadoV1>(), d => d.TipoCambio == TipoCambioProyecto.FUENTE
                ? new Interpretacion(d.Autor, Proyecto, Texto(d.IdProyecto), "CAMBIO_FUENTE", Exitoso, d.FuenteAnterior, d.FuenteNueva)
                : new Interpretacion(d.Autor, Proyecto, Texto(d.IdProyecto), "CAMBIO_" + d.TipoCambio, Exitoso,
                    Texto(d.PresupuestoAnterior), Texto(d.PresupuestoNuevo))),
            "EV-20" => De(sobre.LeerDatos<EstadoOAvanceCambiadoV1>(), d => d.TipoCambio == TipoCambioProyecto.ESTADO
                ? new Interpretacion(d.Autor, Proyecto, Texto(d.IdProyecto), "CAMBIO_ESTADO", Exitoso,
                    d.EstadoAnterior.ToString(), d.EstadoNuevo.ToString())
                : new Interpretacion(d.Autor, Proyecto, Texto(d.IdProyecto), "CAMBIO_" + d.TipoCambio, Exitoso,
                    d.ValorAnterior, d.ValorNuevo)),
            "EV-24" => De(sobre.LeerDatos<ReporteExternoProyectoV1>(), d =>
                new Interpretacion(sistema, Proyecto, Texto(d.IdProyecto), "REPORTE_EXTERNO_" + d.Tipo, Exitoso, null,
                    d.Tipo == TipoReporteExterno.AVANCE ? Texto(d.Avance) : d.Contratista)),
            "EV-21" => De(sobre.LeerDatos<CapaGeograficaActualizadaV1>(), d =>
                new Interpretacion(sistema, CapaGeografica, d.Capa.ToString(), "ACTUALIZACION_CAPA", Exitoso, null,
                    $"v{d.VersionCapa} parte {d.Parte}/{d.Total}")),
            "EV-23" => De(sobre.LeerDatos<EstadoIntegracionCambiadoV1>(), d =>
                new Interpretacion(sistema, Integracion, d.SistemaExterno, "CAMBIO_ESTADO_INTEGRACION", Exitoso,
                    d.EstadoAnterior.ToString(), d.EstadoNuevo.ToString())),
            // Notificación (AD-13, E-37): el estado de entrega queda en el rastro por destinatario (QA-08: consultable).
            "EV-07" => De(sobre.LeerDatos<NotificacionDespachadaV1>(), d =>
                new Interpretacion(sistema, Notificacion, Texto(d.IdNotificacion), "ENTREGA_" + d.EstadoEntrega, Exitoso,
                    null, $"{string.Join(",", d.OrganismosDestino)} por {d.Canal}, {d.Intentos} intento(s)")),
            "EV-28" => De(sobre.LeerDatos<EnvioNotificacionSolicitadoV1>(), d =>
                new Interpretacion(sistema, Notificacion, Texto(d.IdNotificacion), "ENVIO_SOLICITADO", Exitoso, null,
                    $"{d.Organismo} por {d.Canal}, intento {d.Intento}")),
            "EV-29" => De(sobre.LeerDatos<AcuseNotificacionV1>(), d =>
                new Interpretacion(sistema, Notificacion, Texto(d.IdNotificacion), "ACUSE_" + d.Resultado, Exitoso, null,
                    d.Motivo ?? $"intento {d.Intento}")),
            _ => Interpretacion.Desconocida,
        };
    }

    private static Interpretacion De<T>(T datos, Func<T, Interpretacion> leer) => leer(datos);

    private static string Texto(Guid id) => id.ToString();

    private static string? Texto(decimal? valor) => valor?.ToString(CultureInfo.InvariantCulture);
}
