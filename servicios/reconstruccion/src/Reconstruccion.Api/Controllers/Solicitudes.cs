namespace Reconstruccion.Api.Controllers;

// Cuerpos de las peticiones (DTO de entrada).

public sealed record CuerpoLote(Guid IdDesastre, List<Guid> IdsEvaluaciones, string ClaveIdempotencia);

public sealed record CuerpoPresupuesto(decimal? Presupuesto, string? FuenteFinanciacion, DateOnly? Inicio, DateOnly? Fin, string Justificacion);

public sealed record CuerpoResponsable(string Responsable, string Justificacion);

/// <summary>Accion: APROBAR, INICIAR, FINALIZAR o CANCELAR.</summary>
public sealed record CuerpoTransicion(string Accion, string? Justificacion, string? Contratista);

public sealed record CuerpoAvance(decimal? Avance, decimal? Costo, string? Concepto, string? Justificacion);
