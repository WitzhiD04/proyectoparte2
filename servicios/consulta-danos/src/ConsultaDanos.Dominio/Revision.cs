namespace ConsultaDanos.Dominio;

/// <summary>Umbrales configurables de la alarma de R-08 (DA-09): tamaño de la bandeja y antigüedad del conflicto más viejo.</summary>
public sealed record UmbralesRevision(int Conflictos, double Horas)
{
    public static UmbralesRevision PorOmision { get; } = new(50, 24);
}

/// <summary>Indicadores de la bandeja de revisión que Consulta de Daños proyecta (E-13): la bandeja vive en Evaluación de Daños.</summary>
public sealed record IndicadoresRevision(
    int ConflictosPendientes,
    DateTimeOffset? MasViejoDesde,
    double? AntiguedadHoras,
    UmbralesRevision Umbrales,
    bool SuperaTamano,
    bool SuperaAntiguedad)
{
    /// <summary>La consola muestra el aviso cuando se supera cualquiera de los dos umbrales (R-08).</summary>
    public bool Alarma => SuperaTamano || SuperaAntiguedad;

    /// <summary>"Superar" es estrictamente mayor: con 50 conflictos y umbral 50 no hay alarma.</summary>
    public static IndicadoresRevision Calcular(int pendientes, DateTimeOffset? masViejo, DateTimeOffset ahora, UmbralesRevision umbrales)
    {
        double? horas = masViejo is null || pendientes == 0 ? null : Math.Round(Math.Max(0, (ahora - masViejo.Value).TotalHours), 2);
        return new IndicadoresRevision(pendientes, pendientes == 0 ? null : masViejo, horas, umbrales,
            pendientes > umbrales.Conflictos, horas > umbrales.Horas);
    }
}
