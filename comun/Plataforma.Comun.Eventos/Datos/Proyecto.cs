namespace Plataforma.Comun.Eventos.Datos;

// Eventos de Reconstrucción (SAD Cuadro 16 y enmienda E-10) y del adaptador de SE-7 (E-04).

/// <summary>
/// Estado del proyecto después del cambio. Extensión que viaja en EV-18, EV-19 y EV-20 para que los modelos de lectura
/// (AD-15, AD-42) se actualicen sin consultar a Reconstrucción y sin depender del orden de llegada (usan la versión).
/// </summary>
public sealed record ResumenProyecto(
    Guid IdDesastre,
    string Nombre,
    EstadoProyecto Estado,
    decimal? Presupuesto,
    decimal CostoEjecutado,
    decimal Avance,
    int Version);

/// <summary>EV-18, proyecto creado. Extensiones de E-10: CodigosMunicipio, Alcance, Nombre y Proyecto. E-33: Autor.</summary>
public sealed record ProyectoCreadoV1(
    Guid IdProyecto,
    IReadOnlyList<Guid> IdsEvaluacionesOrigen,
    Guid IdDesastre,
    decimal? Presupuesto,
    string? FuenteFinanciacion,
    string? Responsable,
    Cronograma? Cronograma,
    IReadOnlyList<string> CodigosMunicipio,
    AlcanceProyecto Alcance,
    string Nombre,
    ResumenProyecto Proyecto,
    string? Autor = null);

/// <summary>
/// EV-19, presupuesto cambiado. Cubre el presupuesto y la fuente de financiación, que es lo que recibe SE-6 (E-10).
/// Extensiones: TipoCambio, FuenteAnterior, FuenteNueva y Proyecto.
/// </summary>
public sealed record PresupuestoCambiadoV1(
    Guid IdProyecto,
    TipoCambioProyecto TipoCambio,
    decimal? PresupuestoAnterior,
    decimal? PresupuestoNuevo,
    string? FuenteAnterior,
    string? FuenteNueva,
    int VersionHistorial,
    string Autor,
    string Justificacion,
    ResumenProyecto Proyecto);

/// <summary>
/// EV-20, estado, avance o datos del proyecto cambiados: responsable, cronograma, contratista, estado, avance, costo y
/// documento (E-10). Extensiones: TipoCambio, ValorAnterior, ValorNuevo, CostoRegistrado (el monto de este registro),
/// Justificacion y Proyecto.
/// </summary>
public sealed record EstadoOAvanceCambiadoV1(
    Guid IdProyecto,
    TipoCambioProyecto TipoCambio,
    EstadoProyecto EstadoAnterior,
    EstadoProyecto EstadoNuevo,
    decimal Avance,
    decimal CostoEjecutado,
    decimal? CostoRegistrado,
    string? ValorAnterior,
    string? ValorNuevo,
    int VersionHistorial,
    string Autor,
    string Justificacion,
    ResumenProyecto Proyecto);

/// <summary>EV-24 (E-04), avance o contratista reportado por el sistema de proyectos externo (SE-7).</summary>
public sealed record ReporteExternoProyectoV1(
    Guid IdProyecto,
    TipoReporteExterno Tipo,
    decimal? Avance,
    string? Contratista,
    DateTimeOffset InstanteExterno,
    string ClaveOrigen,
    string MarcaAvance);

/// <summary>EV-23 (E-15), el cortacircuitos de un adaptador cambió de estado (mitigación de ATAM R-10).</summary>
public sealed record EstadoIntegracionCambiadoV1(
    string SistemaExterno,
    EstadoIntegracion EstadoAnterior,
    EstadoIntegracion EstadoNuevo,
    DateTimeOffset Instante,
    long MensajesEnCola);
