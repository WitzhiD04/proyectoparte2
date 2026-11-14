using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion;

// Las entidades no salen del dominio: hacia afuera solo viajan estos DTO.

public sealed record DesastreDto(Guid Id, string Nombre, string Estado, bool EnReconstruccion);

public sealed record CandidataDto(Guid IdEvaluacion, Guid IdActivo, string NombreActivo, string TipoActivo, string NivelDano, string Municipio);

public sealed record EvaluacionOrigenDto(Guid IdEvaluacion, Guid IdActivo, string TipoActivo, string NivelDano, bool Vigente);

public sealed record ProyectoDto(
    Guid Id, Guid IdDesastre, Guid IdActivo, string Nombre, string Alcance, string Estado,
    decimal? Presupuesto, string? FuenteFinanciacion, string? Responsable, string? Contratista,
    DateOnly? Inicio, DateOnly? Fin, decimal Avance, decimal CostoEjecutado, bool EnSobrecosto, int Version,
    IReadOnlyList<EvaluacionOrigenDto> Evaluaciones);

public sealed record ResultadoLoteDto(IReadOnlyList<Guid> IdsProyectos, bool Reenvio);

public sealed record DocumentoDto(Guid Id, string Nombre, string Tipo, string Ubicacion, string Hash, string Autor, DateTimeOffset Instante);

public sealed record EntradaHistorialDto(int Version, string TipoCambio, string? ValorAnterior, string? ValorNuevo,
    string Autor, string Justificacion, DateTimeOffset Instante, string Procedencia);

public sealed record HistorialDto(Guid IdProyecto, DateTimeOffset? Fecha, EstadoEnFecha? EstadoEnFecha, IReadOnlyList<EntradaHistorialDto> Entradas);

public sealed record ConsolidadoDto(
    Guid IdDesastre, int TotalProyectos, IReadOnlyDictionary<string, int> ProyectosPorEstado,
    decimal PresupuestoAsignado, decimal CostoEjecutado, decimal AvanceAgregado,
    IReadOnlyList<Guid> ProyectosEnSobrecosto, bool TodosEnEstadoFinal);

public static class Mapeos
{
    public static ProyectoDto ADto(this ProyectoReconstruccion p)
    {
        List<EvaluacionOrigenDto> evaluaciones = new();
        foreach (EvaluacionOrigen e in p.Evaluaciones)
        {
            evaluaciones.Add(new EvaluacionOrigenDto(e.IdEvaluacion, e.IdActivo, e.TipoActivo, e.NivelDano.ToString(), e.Vigente));
        }
        return new ProyectoDto(p.Id, p.IdDesastre, p.IdActivo, p.Nombre, p.Alcance.ToString(), p.Estado.ToString(),
            p.Presupuesto?.Monto, p.FuenteFinanciacion, p.Responsable, p.Contratista,
            p.Cronograma?.Inicio, p.Cronograma?.Fin, p.Avance, p.CostoEjecutado.Monto, p.EnSobrecosto, p.Version, evaluaciones);
    }

    public static EntradaHistorialDto ADto(this EntradaHistorial e)
    {
        return new EntradaHistorialDto(e.Version, e.TipoCambio.ToString(), e.ValorAnterior, e.ValorNuevo,
            e.Autor, e.Justificacion, e.Instante, e.Procedencia);
    }
}
