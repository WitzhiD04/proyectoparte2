using Microsoft.EntityFrameworkCore;

namespace Plataforma.Comun.Mensajeria;

/// <summary>Evento por publicar o ya publicado, en la base de datos del productor (bandeja de salida, AD-45).</summary>
public sealed class FilaBandeja
{
    public Guid Id { get; set; }
    /// <summary>Orden de inserción. Puede tener huecos: no se usa como secuencia del productor.</summary>
    public long Secuencia { get; set; }
    /// <summary>Consecutivo sin huecos por productor. Nulo hasta que el relevo lo asigna.</summary>
    public long? SecuenciaProductor { get; set; }
    public string Tipo { get; set; } = "";
    public string ClaveRuteo { get; set; } = "";
    public string ClaveIdempotencia { get; set; } = "";
    public string? IdCorrelacion { get; set; }
    /// <summary>Sobre canónico serializado, sin la secuencia del productor.</summary>
    public string Sobre { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    /// <summary>
    /// Posición del evento dentro de su transacción. EF Core no inserta en el orden en que se agregan las filas, así
    /// que la numeración ordena por (instante, orden) para respetar el orden de los hechos.
    /// </summary>
    public int Orden { get; set; }
    public DateTimeOffset? PublicadoEn { get; set; }
}

/// <summary>Último número asignado a los eventos de un productor.</summary>
public sealed class FilaContadorProductor
{
    public string Productor { get; set; } = "";
    public long Ultimo { get; set; }
}

/// <summary>Evento ya aplicado por un consumidor. Se escribe en la misma transacción que su efecto.</summary>
public sealed class FilaMensajeProcesado
{
    public string ClaveIdempotencia { get; set; } = "";
    public Guid IdEvento { get; set; }
    public string Tipo { get; set; } = "";
    public DateTimeOffset ProcesadoEn { get; set; }
}

/// <summary>Mapeo de las tablas de mensajería en el DbContext de cada servicio.</summary>
public static class ModeloMensajeria
{
    public const string TablaBandeja = "bandeja_salida";
    public const string TablaContador = "contador_productor";
    public const string TablaProcesados = "mensajes_procesados";

    /// <summary>Para un servicio que publica eventos.</summary>
    public static ModelBuilder ConfigurarBandejaSalida(this ModelBuilder modelo)
    {
        modelo.Entity<FilaBandeja>(e =>
        {
            e.ToTable(TablaBandeja);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Secuencia).HasColumnName("secuencia").UseIdentityAlwaysColumn();
            e.Property(x => x.SecuenciaProductor).HasColumnName("secuencia_productor");
            e.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
            e.Property(x => x.ClaveRuteo).HasColumnName("clave_ruteo").IsRequired();
            e.Property(x => x.ClaveIdempotencia).HasColumnName("clave_idempotencia").IsRequired();
            e.Property(x => x.IdCorrelacion).HasColumnName("id_correlacion");
            e.Property(x => x.Sobre).HasColumnName("sobre").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.PublicadoEn).HasColumnName("publicado_en");
            e.HasIndex(x => x.ClaveIdempotencia).IsUnique();
            e.HasIndex(x => x.SecuenciaProductor).IsUnique();
            e.HasIndex(x => new { x.Instante, x.Orden }).HasFilter("secuencia_productor IS NULL").HasDatabaseName("ix_bandeja_por_numerar");
            e.HasIndex(x => x.SecuenciaProductor).HasFilter("secuencia_productor IS NOT NULL AND publicado_en IS NULL")
                .HasDatabaseName("ix_bandeja_por_publicar");
        });
        modelo.Entity<FilaContadorProductor>(e =>
        {
            e.ToTable(TablaContador);
            e.HasKey(x => x.Productor);
            e.Property(x => x.Productor).HasColumnName("productor");
            e.Property(x => x.Ultimo).HasColumnName("ultimo");
        });
        return modelo;
    }

    /// <summary>Para un servicio que consume eventos.</summary>
    public static ModelBuilder ConfigurarMensajesProcesados(this ModelBuilder modelo)
    {
        modelo.Entity<FilaMensajeProcesado>(e =>
        {
            e.ToTable(TablaProcesados);
            e.HasKey(x => x.ClaveIdempotencia);
            e.Property(x => x.ClaveIdempotencia).HasColumnName("clave_idempotencia");
            e.Property(x => x.IdEvento).HasColumnName("id_evento");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.ProcesadoEn).HasColumnName("procesado_en");
        });
        return modelo;
    }
}
