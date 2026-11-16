namespace Plataforma.Comun.Eventos;

/// <summary>Un evento del catálogo (SAD Cuadro 16 y enmiendas E-01 a E-25).</summary>
/// <param name="Tipo">Identificador del catálogo (EV-nn).</param>
/// <param name="Nombre">Nombre del hecho.</param>
/// <param name="Version">Versión vigente del esquema de datos.</param>
/// <param name="Productor">Quién lo emite.</param>
/// <param name="EnAlcance">Si se implementa en la segunda entrega. Los que no, no tienen esquema todavía.</param>
public sealed record DefinicionEvento(string Tipo, string Nombre, int Version, string Productor, bool EnAlcance)
{
    /// <summary>Clave de ruteo en el exchange de la plataforma: evNN.agregado.hecho (DA-02).</summary>
    public string ClaveRuteo => $"ev{Tipo[3..]}.{Nombre}";

    /// <summary>Nombre del recurso del esquema JSON de los datos.</summary>
    public string Esquema => $"{Tipo}.v{Version}.json";
}

/// <summary>Catálogo de eventos de la plataforma. Es la única fuente de los tipos, nombres y claves de ruteo.</summary>
public static class CatalogoEventos
{
    public const string Exchange = "plataforma.eventos";

    // Productores.
    public const string GestionDesastres = "gestion-desastres";
    public const string Consolidacion = "consolidacion";
    public const string Notificacion = "notificacion";
    public const string EvaluacionDanos = "evaluacion-danos";
    public const string GestionRecursos = "gestion-recursos";
    public const string Reconstruccion = "reconstruccion";
    public const string AdaptadorSe1 = "adaptador-se1";
    public const string AdaptadorSe2 = "adaptador-se2";
    public const string AdaptadorSe4 = "adaptador-se4";
    public const string AdaptadorSe6 = "adaptador-se6";
    public const string AdaptadorSe7 = "adaptador-se7";
    public const string AdaptadorSe8 = "adaptador-se8";

    // SAD, Cuadro 16. Fuera de alcance: Consolidación y Recursos (sección 2.3 de la revisión). Notificación entra (RF-GD-06).
    public static readonly DefinicionEvento ReporteRecibido = new("EV-01", "reporte.recibido", 1, AdaptadorSe1, false);
    public static readonly DefinicionEvento ReporteConsolidado = new("EV-02", "reporte.consolidado", 1, Consolidacion, false);
    public static readonly DefinicionEvento DesastreCreado = new("EV-03", "desastre.creado", 1, GestionDesastres, true);
    public static readonly DefinicionEvento SeveridadCambiada = new("EV-04", "desastre.severidad-cambiada", 1, GestionDesastres, true);
    public static readonly DefinicionEvento ZonaActualizada = new("EV-05", "desastre.zona-actualizada", 1, GestionDesastres, true);
    public static readonly DefinicionEvento EstadoDesastreCambiado = new("EV-06", "desastre.estado-cambiado", 1, GestionDesastres, true);
    public static readonly DefinicionEvento NotificacionDespachada = new("EV-07", "notificacion.despachada", 1, Notificacion, true);
    public static readonly DefinicionEvento EvaluacionValidada = new("EV-08", "evaluacion.validada", 1, EvaluacionDanos, true);
    public static readonly DefinicionEvento EvaluacionSincronizada = new("EV-09", "evaluacion.sincronizada", 1, EvaluacionDanos, true);
    public static readonly DefinicionEvento ConflictoDetectado = new("EV-10", "evaluacion.conflicto-detectado", 1, EvaluacionDanos, true);
    public static readonly DefinicionEvento InventarioActualizado = new("EV-11", "recurso.inventario-actualizado", 1, AdaptadorSe4, false);
    public static readonly DefinicionEvento RecursoRegistrado = new("EV-12", "recurso.registrado", 1, GestionRecursos, false);
    public static readonly DefinicionEvento SolicitudConfirmacionReserva = new("EV-13", "recurso.solicitud-confirmacion-reserva", 1, GestionRecursos, false);
    public static readonly DefinicionEvento ReservaConfirmada = new("EV-14", "recurso.reserva-confirmada", 1, AdaptadorSe4, false);
    public static readonly DefinicionEvento ReservaRechazada = new("EV-15", "recurso.reserva-rechazada", 1, AdaptadorSe4, false);
    public static readonly DefinicionEvento ReservaExpirada = new("EV-16", "recurso.reserva-expirada", 1, GestionRecursos, false);
    public static readonly DefinicionEvento RecursoAsignado = new("EV-17", "recurso.asignado", 1, GestionRecursos, false);
    public static readonly DefinicionEvento ProyectoCreado = new("EV-18", "proyecto.creado", 1, Reconstruccion, true);
    public static readonly DefinicionEvento PresupuestoCambiado = new("EV-19", "proyecto.presupuesto-cambiado", 1, Reconstruccion, true);
    public static readonly DefinicionEvento EstadoOAvanceCambiado = new("EV-20", "proyecto.estado-avance-cambiado", 1, Reconstruccion, true);

    // Extensiones aditivas (ENMIENDAS_DOCUMENTOS.md).
    public static readonly DefinicionEvento CapaGeograficaActualizada = new("EV-21", "gis.capa-actualizada", 1, AdaptadorSe2, true);
    public static readonly DefinicionEvento ConflictoResuelto = new("EV-22", "evaluacion.conflicto-resuelto", 1, EvaluacionDanos, true);
    public static readonly DefinicionEvento EstadoIntegracionCambiado = new("EV-23", "integracion.estado-cambiado", 1, "adaptador-*", true);
    public static readonly DefinicionEvento ReporteExternoProyecto = new("EV-24", "proyecto.reporte-externo", 1, AdaptadorSe7, true);
    public static readonly DefinicionEvento TipoActivoActualizado = new("EV-25", "catalogo.tipo-activo-actualizado", 1, EvaluacionDanos, true);
    public static readonly DefinicionEvento EvaluacionActualizada = new("EV-26", "evaluacion.actualizada", 1, EvaluacionDanos, true);
    public static readonly DefinicionEvento OrganismosDesastreCambiados = new("EV-27", "desastre.organismos-cambiados", 1, GestionDesastres, true);
    public static readonly DefinicionEvento EnvioNotificacionSolicitado = new("EV-28", "notificacion.envio-solicitado", 1, Notificacion, true);
    public static readonly DefinicionEvento AcuseNotificacion = new("EV-29", "notificacion.acuse-recibido", 1, AdaptadorSe8, true);

    public static IReadOnlyList<DefinicionEvento> Todos { get; } =
    [
        ReporteRecibido, ReporteConsolidado, DesastreCreado, SeveridadCambiada, ZonaActualizada, EstadoDesastreCambiado,
        NotificacionDespachada, EvaluacionValidada, EvaluacionSincronizada, ConflictoDetectado, InventarioActualizado,
        RecursoRegistrado, SolicitudConfirmacionReserva, ReservaConfirmada, ReservaRechazada, ReservaExpirada, RecursoAsignado,
        ProyectoCreado, PresupuestoCambiado, EstadoOAvanceCambiado, CapaGeograficaActualizada, ConflictoResuelto,
        EstadoIntegracionCambiado, ReporteExternoProyecto, TipoActivoActualizado, EvaluacionActualizada, OrganismosDesastreCambiados,
        EnvioNotificacionSolicitado, AcuseNotificacion,
    ];

    public static IReadOnlyList<DefinicionEvento> EnAlcance { get; } = Todos.Where(e => e.EnAlcance).ToList();

    public static DefinicionEvento PorTipo(string tipo) =>
        Todos.FirstOrDefault(e => e.Tipo == tipo) ?? throw new KeyNotFoundException($"El evento {tipo} no está en el catálogo.");
}
