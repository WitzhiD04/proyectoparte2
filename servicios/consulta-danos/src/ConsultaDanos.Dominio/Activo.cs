namespace ConsultaDanos.Dominio;

/// <summary>
/// De qué evento sale el contenido vigente de un activo. Ordena dos eventos que traen la misma versión del activo:
/// gana el de rango mayor. Así la proyección queda igual sin importar el orden de llegada (dos réplicas consumen la cola).
/// </summary>
public enum OrigenContenido
{
    /// <summary>EV-08 llegó antes que la evaluación: su copia del contenido sirve mientras llega EV-09 (versión 0).</summary>
    Validacion = 0,

    /// <summary>
    /// EV-09 de una evaluación en conflicto. Si la regla de conflicto no la aplicó, trae la versión del activo que ya
    /// estaba, con el contenido entrante: no debe reemplazar el contenido vigente de esa versión (AD-32).
    /// </summary>
    SincronizacionEnConflicto = 1,

    /// <summary>EV-09 aplicada o EV-26 (actualización o asociación en el servidor).</summary>
    Evaluacion = 2,

    /// <summary>EV-22: la decisión humana dice cuál es la evaluación vigente de esa versión (AD-32, AD-33).</summary>
    Resolucion = 3,
}

/// <summary>El contenido de la evaluación vigente de un activo, tal como lo trae el evento.</summary>
public sealed record ContenidoActivo(
    Guid IdEvaluacion,
    Guid? IdDesastre,
    string TipoActivo,
    NivelDano Nivel,
    string CodigoMunicipio,
    double Lat,
    double Lon,
    double? PrecisionM,
    string EstadoEvaluacion,
    int VersionEvaluacion);

/// <summary>Grupo del consolidado de RF-ED-10: desastre × municipio × tipo de activo × nivel.</summary>
public sealed record ClaveZona(Guid IdDesastre, string CodigoMunicipio, string TipoActivo, NivelDano Nivel)
    : IComparable<ClaveZona>
{
    /// <summary>Orden total de las claves: las filas del consolidado se actualizan siempre en este orden, sin interbloqueos.</summary>
    public int CompareTo(ClaveZona? otra)
    {
        if (otra is null) return 1;
        int c = IdDesastre.CompareTo(otra.IdDesastre);
        if (c != 0) return c;
        c = string.CompareOrdinal(CodigoMunicipio, otra.CodigoMunicipio);
        if (c != 0) return c;
        c = string.CompareOrdinal(TipoActivo, otra.TipoActivo);
        return c != 0 ? c : Nivel.CompareTo(otra.Nivel);
    }
}

/// <summary>
/// Estado de un activo en <c>p_evaluacion_vigente</c> (DA-08). Tiene tres aspectos que llegan por eventos distintos y en
/// cualquier orden: el contenido (protegido por la versión del activo), la validación de su evaluación (protegida por la
/// versión de la evaluación, en <c>p_validaciones</c>) y la fusión como duplicado (E-14, definitiva).
/// </summary>
public sealed record EstadoActivo(
    Guid IdActivo,
    ContenidoActivo? Contenido,
    int VersionActivo,
    OrigenContenido Origen,
    EstadoValidacion? Validacion,
    bool Fusionado,
    Guid? FusionadoEn)
{
    /// <summary>Un activo del que todavía no llegó ningún contenido (versión -1).</summary>
    public static EstadoActivo Vacio(Guid idActivo) => new(idActivo, null, -1, OrigenContenido.Validacion, null, false, null);

    public bool Validada => Validacion == EstadoValidacion.VALIDADA;

    /// <summary>
    /// Si un contenido con esta versión y este origen reemplaza al actual: una versión mayor siempre; la misma versión,
    /// solo si el origen tiene más rango. Un evento repetido o atrasado no cambia nada.
    /// </summary>
    public bool Reemplaza(int version, OrigenContenido origen) =>
        version > VersionActivo || (version == VersionActivo && origen > Origen);

    /// <summary>
    /// Aplica un contenido. Si cambia la evaluación vigente, la validación es la de la nueva evaluación, que da quien
    /// llama (la tiene <c>p_validaciones</c>); si es la misma evaluación, se conserva.
    /// </summary>
    public EstadoActivo ConContenido(ContenidoActivo contenido, int version, OrigenContenido origen,
        Func<Guid, EstadoValidacion?> validacionDe)
    {
        if (!Reemplaza(version, origen))
        {
            return this;
        }
        EstadoValidacion? validacion = Contenido?.IdEvaluacion == contenido.IdEvaluacion ? Validacion : validacionDe(contenido.IdEvaluacion);
        return this with { Contenido = contenido, VersionActivo = version, Origen = origen, Validacion = validacion };
    }

    /// <summary>La validación de una evaluación cambió: solo afecta al activo si esa es su evaluación vigente.</summary>
    public EstadoActivo ConValidacion(Guid idEvaluacion, EstadoValidacion? validacion) =>
        Contenido?.IdEvaluacion == idEvaluacion ? this with { Validacion = validacion } : this;

    /// <summary>Fusionado como duplicado de otro activo (E-14): deja de contar en el mapa y en el consolidado. No se borra.</summary>
    public EstadoActivo FusionarEn(Guid idActivoVigente) => this with { Fusionado = true, FusionadoEn = idActivoVigente };

    /// <summary>Si el activo cuenta en el mapa: tiene contenido, un desastre y no es un duplicado fusionado.</summary>
    public bool Visible => Contenido is { IdDesastre: not null } && !Fusionado;

    /// <summary>El grupo del consolidado en que cuenta, o nulo. Solo cuentan las evaluaciones validadas (RF-ED-10).</summary>
    public ClaveZona? ClaveConsolidado => Visible && Validada
        ? new ClaveZona(Contenido!.IdDesastre!.Value, Contenido.CodigoMunicipio, Contenido.TipoActivo, Contenido.Nivel)
        : null;
}

public static class Consolidacion
{
    /// <summary>
    /// Lo que cambia en <c>p_dano_por_zona</c> al pasar el activo de un estado a otro: -1 en el grupo donde contaba y +1
    /// en el grupo donde cuenta ahora. Cada activo se actualiza bajo su propio bloqueo, así que los incrementos son
    /// exactos aunque dos réplicas apliquen eventos a la vez. Va ordenado por clave para tomar los bloqueos siempre igual.
    /// </summary>
    public static IReadOnlyList<(ClaveZona Clave, int Delta)> Deltas(EstadoActivo antes, EstadoActivo despues) =>
        Deltas([(antes, despues)]);

    /// <summary>Los cambios de varias transiciones del mismo evento, sumados por clave y sin los que se anulan.</summary>
    public static IReadOnlyList<(ClaveZona Clave, int Delta)> Deltas(IEnumerable<(EstadoActivo Antes, EstadoActivo Despues)> transiciones)
    {
        Dictionary<ClaveZona, int> suma = [];
        foreach ((EstadoActivo antes, EstadoActivo despues) in transiciones)
        {
            if (antes.ClaveConsolidado is { } a) suma[a] = suma.GetValueOrDefault(a) - 1;
            if (despues.ClaveConsolidado is { } d) suma[d] = suma.GetValueOrDefault(d) + 1;
        }
        return suma.Where(x => x.Value != 0).Select(x => (x.Key, x.Value)).OrderBy(x => x.Key).ToList();
    }

    /// <summary>Desastres cuyo mapa o consolidado cambia con la transición (el de antes y el de ahora).</summary>
    public static IReadOnlyCollection<Guid> DesastresAfectados(EstadoActivo antes, EstadoActivo despues)
    {
        if (antes == despues)
        {
            return [];
        }
        return new[] { antes.Contenido?.IdDesastre, despues.Contenido?.IdDesastre }
            .Where(d => d is not null).Select(d => d!.Value).Distinct().ToList();
    }
}
