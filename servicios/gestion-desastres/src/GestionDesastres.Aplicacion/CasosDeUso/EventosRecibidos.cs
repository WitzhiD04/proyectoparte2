namespace GestionDesastres.Aplicacion.CasosDeUso;

/// <summary>
/// Aplica a la copia local el estado de un proyecto de reconstrucción (EV-18 y EV-20, E-07). El adaptador de entrada
/// traduce el evento; este caso de uso no conoce el formato del bus.
/// </summary>
public sealed class ActualizarProyectoDelDesastre
{
    private readonly IProyectosDelDesastre _proyectos;
    public ActualizarProyectoDelDesastre(IProyectosDelDesastre proyectos) { _proyectos = proyectos; }

    public Task EjecutarAsync(Guid idProyecto, Guid idDesastre, bool final, int version, CancellationToken ct = default) =>
        _proyectos.AplicarAsync(idProyecto, idDesastre, final, version, ct);
}

/// <summary>Guarda una parte de la capa de municipios recibida de SE-2 (EV-21, DA-07).</summary>
public sealed class ActualizarCapaMunicipios
{
    private readonly ICapaMunicipios _capa;
    public ActualizarCapaMunicipios(ICapaMunicipios capa) { _capa = capa; }

    public Task EjecutarAsync(IReadOnlyList<DivisionGeografica> divisiones, int versionCapa, CancellationToken ct = default) =>
        _capa.AplicarAsync(divisiones, versionCapa, ct);
}
