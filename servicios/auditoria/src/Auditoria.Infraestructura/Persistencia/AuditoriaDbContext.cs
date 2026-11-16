using Microsoft.EntityFrameworkCore;
using Plataforma.Comun.Mensajeria;

namespace Auditoria.Infraestructura.Persistencia;

/// <summary>
/// Base de datos del servicio de Auditoría (AD-01: una base por servicio). El modelo describe columnas, tipos,
/// restricciones e índices y las migraciones de EF Core los crean (DT-19). Lo que EF no sabe expresar está en la
/// migración inicial, escrito a mano: la tabla "registro" particionada por mes (QA-13 e, retención de 5 años o más),
/// su partición por omisión, la función que crea particiones por adelantado y los permisos de mínimo privilegio (AD-41).
/// </summary>
public sealed class AuditoriaDbContext : DbContext
{
    public const string TablaRegistro = "registro";
    public const string TablaSeguimiento = "seguimiento_productor";
    public const string TablaHuecos = "huecos_secuencia";

    public AuditoriaDbContext(DbContextOptions<AuditoriaDbContext> opciones) : base(opciones) { }

    public DbSet<FilaRegistro> Registro => Set<FilaRegistro>();
    public DbSet<FilaSeguimiento> Seguimientos => Set<FilaSeguimiento>();
    public DbSet<FilaHueco> Huecos => Set<FilaHueco>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<FilaRegistro>(e =>
        {
            e.ToTable(TablaRegistro, t =>
            {
                // "Rechazado" queda admitido desde ya para los rechazos de autorización (SAD §7.1, DA-15).
                t.HasCheckConstraint("ck_registro_resultado", "resultado IS NULL OR resultado IN ('Exitoso', 'Rechazado')");
                t.HasCheckConstraint("ck_registro_secuencia", "secuencia_productor IS NULL OR secuencia_productor >= 1");
                t.HasCheckConstraint("ck_registro_version", "version >= 1");
            });
            // La llave incluye la columna de partición, como exige PostgreSQL en una tabla particionada. El instante de
            // emisión es parte del evento y no cambia al reentregarlo, así que la llave sigue siendo única por evento:
            // es la tercera barrera de idempotencia, después de la clave del consumidor y de la consulta por id.
            e.HasKey(x => new { x.IdEvento, x.InstanteEmision }).HasName("pk_registro");
            e.Property(x => x.IdEvento).HasColumnName("id_evento").ValueGeneratedNever();
            e.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Productor).HasColumnName("productor").IsRequired();
            e.Property(x => x.SecuenciaProductor).HasColumnName("secuencia_productor");
            e.Property(x => x.InstanteEmision).HasColumnName("instante_emision");
            e.Property(x => x.InstanteRegistro).HasColumnName("instante_registro");
            e.Property(x => x.IdCorrelacion).HasColumnName("id_correlacion");
            e.Property(x => x.ClaveIdempotencia).HasColumnName("clave_idempotencia").IsRequired();
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.Entidad).HasColumnName("entidad");
            e.Property(x => x.IdEntidad).HasColumnName("id_entidad");
            e.Property(x => x.Accion).HasColumnName("accion");
            e.Property(x => x.Resultado).HasColumnName("resultado");
            e.Property(x => x.ValorAnterior).HasColumnName("valor_anterior");
            e.Property(x => x.ValorNuevo).HasColumnName("valor_nuevo");
            e.Property(x => x.Procedencia).HasColumnName("procedencia").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Sobre).HasColumnName("sobre").HasColumnType("jsonb").IsRequired();

            // Índices de la consulta del rastro (QA-13 d: 10 000 entradas en p95 < 10 s). En una tabla particionada
            // cada índice se crea en todas las particiones, también en las que se agreguen después.
            e.HasIndex(x => new { x.Entidad, x.IdEntidad, x.InstanteEmision }).HasDatabaseName("ix_registro_entidad");
            e.HasIndex(x => new { x.Actor, x.InstanteEmision }).HasDatabaseName("ix_registro_actor");
            e.HasIndex(x => new { x.Tipo, x.InstanteEmision }).HasDatabaseName("ix_registro_tipo");
            e.HasIndex(x => x.InstanteEmision).HasDatabaseName("ix_registro_instante");
            // Trazas de punta a punta (DA-20: "Auditoría lo indexa").
            e.HasIndex(x => x.IdCorrelacion).HasFilter("id_correlacion IS NOT NULL").HasDatabaseName("ix_registro_correlacion");
            // Detección de huecos (R-02): números de cada productor en orden.
            e.HasIndex(x => new { x.Productor, x.SecuenciaProductor }).HasFilter("secuencia_productor IS NOT NULL")
                .HasDatabaseName("ix_registro_secuencia");
        });

        modelo.Entity<FilaSeguimiento>(e =>
        {
            e.ToTable(TablaSeguimiento, t =>
                t.HasCheckConstraint("ck_seguimiento_orden", "mayor_contiguo <= revisado_hasta AND mayor_recibido <= revisado_hasta"));
            e.HasKey(x => x.Productor);
            e.Property(x => x.Productor).HasColumnName("productor");
            e.Property(x => x.MayorContiguo).HasColumnName("mayor_contiguo");
            e.Property(x => x.MayorRecibido).HasColumnName("mayor_recibido");
            e.Property(x => x.RevisadoHasta).HasColumnName("revisado_hasta");
            e.Property(x => x.RevisadoEn).HasColumnName("revisado_en");
        });

        modelo.Entity<FilaHueco>(e =>
        {
            e.ToTable(TablaHuecos, t =>
            {
                t.HasCheckConstraint("ck_huecos_secuencia", "secuencia >= 1");
                t.HasCheckConstraint("ck_huecos_reportado", "reportado_en IS NULL OR reportado_en >= detectado_en");
                t.HasCheckConstraint("ck_huecos_cubierto", "cubierto_en IS NULL OR cubierto_en >= detectado_en");
            });
            e.HasKey(x => new { x.Productor, x.Secuencia });
            e.Property(x => x.Productor).HasColumnName("productor");
            e.Property(x => x.Secuencia).HasColumnName("secuencia");
            e.Property(x => x.DetectadoEn).HasColumnName("detectado_en");
            e.Property(x => x.ReportadoEn).HasColumnName("reportado_en");
            e.Property(x => x.CubiertoEn).HasColumnName("cubierto_en");
            e.HasIndex(x => x.Productor).HasFilter("cubierto_en IS NULL").HasDatabaseName("ix_huecos_abiertos");
            e.HasIndex(x => x.ReportadoEn).HasFilter("reportado_en IS NOT NULL").HasDatabaseName("ix_huecos_reportados");
        });

        // Eventos ya aplicados: consumo idempotente por claveIdempotencia (DA-03).
        modelo.ConfigurarMensajesProcesados();
    }
}
