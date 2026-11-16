namespace Auditoria.Infraestructura.Persistencia;

/// <summary>Fila de la tabla particionada "registro" (AD-21). Solo se inserta y se lee.</summary>
public sealed class FilaRegistro
{
    public Guid IdEvento { get; set; }
    public string Tipo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Version { get; set; }
    public string Productor { get; set; } = "";
    public long? SecuenciaProductor { get; set; }
    public DateTimeOffset InstanteEmision { get; set; }
    public DateTimeOffset InstanteRegistro { get; set; }
    public string? IdCorrelacion { get; set; }
    public string ClaveIdempotencia { get; set; } = "";
    public string? Actor { get; set; }
    public string? Entidad { get; set; }
    public string? IdEntidad { get; set; }
    public string? Accion { get; set; }
    public string? Resultado { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    /// <summary>Procedencia (AD-11) en JSON: fuente, sistemaOrigen y confianza.</summary>
    public string Procedencia { get; set; } = "{}";
    /// <summary>Sobre canónico completo en JSON.</summary>
    public string Sobre { get; set; } = "{}";
}

/// <summary>Estado de la secuencia de un productor (R-02).</summary>
public sealed class FilaSeguimiento
{
    public string Productor { get; set; } = "";
    public long MayorContiguo { get; set; }
    public long MayorRecibido { get; set; }
    public long RevisadoHasta { get; set; }
    public DateTimeOffset? RevisadoEn { get; set; }
}

/// <summary>Un número faltante en la secuencia de un productor (registro HUECO_DETECTADO de DA-17).</summary>
public sealed class FilaHueco
{
    public string Productor { get; set; } = "";
    public long Secuencia { get; set; }
    public DateTimeOffset DetectadoEn { get; set; }
    public DateTimeOffset? ReportadoEn { get; set; }
    public DateTimeOffset? CubiertoEn { get; set; }
}
