namespace ConsultaDanos.Dominio;

/// <summary>Activos validados de un tipo en un nivel, dentro de una zona (una fila de <c>p_dano_por_zona</c>).</summary>
public sealed record ConteoTipo(string TipoActivo, string? NombreTipo, NivelDano Nivel, int Cantidad);

/// <summary>Una zona (municipio) del consolidado de un desastre, con su impacto por tipo de activo (RF-ED-10).</summary>
public sealed record ZonaConsolidada(string CodigoMunicipio, string? Nombre, string? CodigoDepartamento, IReadOnlyList<ConteoTipo> Conteos)
{
    public Distribucion Total => Conteos.Aggregate(new Distribucion(), (d, c) => d.Sumar(c.Nivel, c.Cantidad));

    public Distribucion De(string tipoActivo) =>
        Conteos.Where(c => c.TipoActivo == tipoActivo).Aggregate(new Distribucion(), (d, c) => d.Sumar(c.Nivel, c.Cantidad));

    /// <summary>Activos de tipo crítico en nivel 4 o 5: el primer criterio de la priorización.</summary>
    public int CriticosGraves(IReadOnlySet<string> tiposCriticos) =>
        Conteos.Where(c => tiposCriticos.Contains(c.TipoActivo) && c.Nivel >= NivelDano.GRAVE).Sum(c => c.Cantidad);
}

/// <summary>Una zona con su lugar en la priorización (1 es la primera que se atiende).</summary>
public sealed record ZonaPriorizada(int Prioridad, ZonaConsolidada Zona, Distribucion Total, double? Indice, int CriticosGraves);

/// <summary>
/// Priorización de RF-ED-10 (DA-08, confirmada en P-05): las zonas se ordenan por la cantidad de activos de tipo
/// crítico (hospital, escuela, puente, red de agua) en nivel 4 o 5 y después por el índice de daño. Los dos criterios
/// van de mayor a menor. Para que el orden sea estable entre réplicas y respuestas, un empate se rompe por la cantidad
/// de activos y por el código DIVIPOLA.
/// </summary>
public static class Priorizacion
{
    /// <summary>Tipos críticos por omisión: los cuatro de DA-08 con los códigos del catálogo inicial (en mayúsculas, E-08).</summary>
    public static IReadOnlySet<string> TiposCriticosPorOmision { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "HOSPITAL", "ESCUELA", "PUENTE", "SISTEMA_AGUA" };

    public static IReadOnlyList<ZonaPriorizada> Ordenar(IEnumerable<ZonaConsolidada> zonas, IReadOnlySet<string> tiposCriticos)
    {
        return zonas
            .Select(z => (Zona: z, Total: z.Total, Criticos: z.CriticosGraves(tiposCriticos)))
            .Where(x => x.Total.Total > 0)
            .OrderByDescending(x => x.Criticos)
            .ThenByDescending(x => x.Total.Indice ?? 0)
            .ThenByDescending(x => x.Total.Total)
            .ThenBy(x => x.Zona.CodigoMunicipio, StringComparer.Ordinal)
            .Select((x, i) => new ZonaPriorizada(i + 1, x.Zona, x.Total, x.Total.Indice, x.Criticos))
            .ToList();
    }
}
