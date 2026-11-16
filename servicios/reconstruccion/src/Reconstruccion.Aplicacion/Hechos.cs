using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion;

/// <summary>
/// Estado de un proyecto después de un cambio, con lo que los demás módulos necesitan saber de él.
/// Es un dato de la aplicación: no conoce el formato del bus. El adaptador de salida lo traduce al contrato.
/// </summary>
public sealed record ResumenDeProyecto(
    Guid IdProyecto,
    Guid IdDesastre,
    string Nombre,
    AlcanceTerritorial Alcance,
    EstadoProyecto Estado,
    decimal? Presupuesto,
    string? FuenteFinanciacion,
    string? Responsable,
    Cronograma? Cronograma,
    decimal CostoEjecutado,
    decimal Avance,
    int Version,
    IReadOnlyList<Guid> IdsEvaluacionesOrigen,
    IReadOnlyList<string> CodigosMunicipio);

/// <summary>
/// Un hecho de negocio ocurrido en un proyecto: una entrada nueva del historial más el estado resultante.
/// <paramref name="CostoRegistrado"/> es el monto del costo registrado en ese cambio, si el cambio fue un costo.
/// </summary>
public sealed record HechoProyecto(EntradaHistorial Entrada, ResumenDeProyecto Proyecto, decimal? CostoRegistrado);

public static class HechosDeProyecto
{
    /// <summary>Un hecho por cada entrada de historial nueva del proyecto (cada cambio deja su entrada).</summary>
    public static List<HechoProyecto> De(ProyectoReconstruccion proyecto)
    {
        ResumenDeProyecto resumen = new(
            proyecto.Id, proyecto.IdDesastre, proyecto.Nombre, proyecto.Alcance, proyecto.Estado,
            proyecto.Presupuesto?.Monto, proyecto.FuenteFinanciacion, proyecto.Responsable, proyecto.Cronograma,
            proyecto.CostoEjecutado.Monto, proyecto.Avance, proyecto.Version,
            proyecto.Evaluaciones.Select(e => e.IdEvaluacion).ToList(), proyecto.CodigosMunicipio);

        // Cada RegistrarCosto deja una entrada de tipo Costo y un registro de costo, en el mismo orden.
        Queue<RegistroCosto> costos = new(proyecto.CostosNuevos);
        List<HechoProyecto> hechos = new();
        foreach (EntradaHistorial entrada in proyecto.EntradasNuevas)
        {
            decimal? costo = entrada.TipoCambio == TipoCambio.Costo && costos.Count > 0 ? costos.Dequeue().Monto.Monto : null;
            hechos.Add(new HechoProyecto(entrada, resumen, costo));
        }
        return hechos;
    }
}
