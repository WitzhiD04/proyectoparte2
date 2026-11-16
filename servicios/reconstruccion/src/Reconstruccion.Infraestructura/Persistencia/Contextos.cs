using Microsoft.EntityFrameworkCore;
using Plataforma.Comun.Mensajeria;

namespace Reconstruccion.Infraestructura.Persistencia;

/// <summary>
/// Base de datos del servicio de Reconstrucción. El modelo describe el esquema completo (tipos, restricciones e
/// índices) y las migraciones de EF Core lo crean (DT-19). Los permisos de mínimo privilegio, que garantizan el
/// historial de solo inserción (AD-41), están en la migración inicial.
/// </summary>
public sealed class ReconstruccionDbContext : DbContext
{
    public ReconstruccionDbContext(DbContextOptions<ReconstruccionDbContext> opciones) : base(opciones) { }

    public DbSet<ProyectoFila> Proyectos => Set<ProyectoFila>();
    public DbSet<EvaluacionOrigenFila> EvaluacionesOrigen => Set<EvaluacionOrigenFila>();
    public DbSet<EntradaHistorialFila> Historial => Set<EntradaHistorialFila>();
    public DbSet<RegistroAvanceFila> Avances => Set<RegistroAvanceFila>();
    public DbSet<RegistroCostoFila> Costos => Set<RegistroCostoFila>();
    public DbSet<DocumentoFila> Documentos => Set<DocumentoFila>();
    public DbSet<LoteCreacionFila> Lotes => Set<LoteCreacionFila>();
    public DbSet<DesastreLocalFila> Desastres => Set<DesastreLocalFila>();
    public DbSet<EvaluacionValidadaFila> Evaluaciones => Set<EvaluacionValidadaFila>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<ProyectoFila>(e =>
        {
            e.ToTable("proyectos", t =>
            {
                t.HasCheckConstraint("ck_proyectos_alcance", "alcance IN ('Municipal', 'Regional')");
                t.HasCheckConstraint("ck_proyectos_estado", "estado IN ('Formulado', 'Aprobado', 'EnEjecucion', 'Finalizado', 'Cancelado')");
                t.HasCheckConstraint("ck_proyectos_presupuesto", "presupuesto >= 0");
                t.HasCheckConstraint("ck_proyectos_avance", "avance BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_proyectos_costo", "costo_ejecutado >= 0");
                t.HasCheckConstraint("ck_proyectos_cronograma", "fin IS NULL OR inicio IS NULL OR fin > inicio");
            });
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(p => p.IdDesastre).HasColumnName("id_desastre");
            e.Property(p => p.IdActivo).HasColumnName("id_activo");
            e.Property(p => p.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(p => p.Alcance).HasColumnName("alcance").IsRequired();
            e.Property(p => p.Estado).HasColumnName("estado").IsRequired();
            e.Property(p => p.Presupuesto).HasColumnName("presupuesto").HasPrecision(18, 2);
            e.Property(p => p.FuenteFinanciacion).HasColumnName("fuente_financiacion");
            e.Property(p => p.Responsable).HasColumnName("responsable");
            e.Property(p => p.Contratista).HasColumnName("contratista");
            e.Property(p => p.Inicio).HasColumnName("inicio");
            e.Property(p => p.Fin).HasColumnName("fin");
            e.Property(p => p.Avance).HasColumnName("avance").HasPrecision(5, 2);
            e.Property(p => p.CostoEjecutado).HasColumnName("costo_ejecutado").HasPrecision(18, 2);
            // Si dos usuarios cambian el mismo proyecto a la vez, el segundo guardado falla en vez de pisar al primero.
            e.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasMany(p => p.Evaluaciones).WithOne().HasForeignKey(x => x.IdProyecto);
            e.HasIndex(p => p.IdDesastre).HasDatabaseName("ix_proyectos_desastre");
        });

        modelo.Entity<EvaluacionOrigenFila>(e =>
        {
            e.ToTable("evaluaciones_origen");
            e.HasKey(x => new { x.IdProyecto, x.IdEvaluacion });
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.IdEvaluacion).HasColumnName("id_evaluacion");
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo").IsRequired();
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano").IsRequired();
            e.Property(x => x.CodigoMunicipio).HasColumnName("codigo_municipio").IsRequired();
            e.Property(x => x.Vigente).HasColumnName("vigente");
            // Una evaluación está vinculada como máximo a un proyecto que no esté cancelado.
            e.HasIndex(x => x.IdEvaluacion).IsUnique().HasFilter("vigente").HasDatabaseName("ux_evaluacion_vigente");
        });

        modelo.Entity<EntradaHistorialFila>(e =>
        {
            e.ToTable("historial");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.TipoCambio).HasColumnName("tipo_cambio").IsRequired();
            e.Property(x => x.ValorAnterior).HasColumnName("valor_anterior");
            e.Property(x => x.ValorNuevo).HasColumnName("valor_nuevo");
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Justificacion).HasColumnName("justificacion").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Procedencia).HasColumnName("procedencia").IsRequired();
            // Llave foránea a proyectos: así EF inserta el proyecto antes que sus registros.
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
            e.HasIndex(x => new { x.IdProyecto, x.Version }).IsUnique();
            e.HasIndex(x => new { x.IdProyecto, x.Instante }).HasDatabaseName("ix_historial_proyecto_instante");
        });

        modelo.Entity<RegistroAvanceFila>(e =>
        {
            e.ToTable("registros_avance", t => t.HasCheckConstraint("ck_registros_avance_porcentaje", "porcentaje BETWEEN 0 AND 100"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Porcentaje).HasColumnName("porcentaje").HasPrecision(5, 2);
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Procedencia).HasColumnName("procedencia").IsRequired();
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<RegistroCostoFila>(e =>
        {
            e.ToTable("registros_costo", t => t.HasCheckConstraint("ck_registros_costo_monto", "monto > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Monto).HasColumnName("monto").HasPrecision(18, 2);
            e.Property(x => x.Concepto).HasColumnName("concepto").IsRequired();
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<DocumentoFila>(e =>
        {
            e.ToTable("documentos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion").IsRequired();
            e.Property(x => x.Hash).HasColumnName("hash").IsRequired();
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<LoteCreacionFila>(e =>
        {
            e.ToTable("lotes_creacion");
            e.HasKey(x => x.ClaveIdempotencia);
            e.Property(x => x.ClaveIdempotencia).HasColumnName("clave_idempotencia");
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.IdsProyectos).HasColumnName("ids_proyectos").IsRequired();
        });

        // Copias locales de datos de otros módulos (DA-01).
        modelo.Entity<DesastreLocalFila>(e =>
        {
            e.ToTable("desastres_locales");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.Estado).HasColumnName("estado").IsRequired();
        });

        modelo.Entity<EvaluacionValidadaFila>(e =>
        {
            e.ToTable("evaluaciones_validadas", t => t.HasCheckConstraint("ck_evaluaciones_validadas_nivel",
                "nivel_dano IN ('SinDano', 'Leve', 'Moderado', 'Grave', 'DestruccionTotal')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.NombreActivo).HasColumnName("nombre_activo").IsRequired();
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo").IsRequired();
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano").IsRequired();
            e.Property(x => x.Municipio).HasColumnName("municipio").IsRequired();
            e.Property(x => x.CodigoMunicipio).HasColumnName("codigo_municipio").IsRequired();
            e.Property(x => x.Estado).HasColumnName("estado").IsRequired();
            e.HasOne<DesastreLocalFila>().WithMany().HasForeignKey(x => x.IdDesastre);
            e.HasIndex(x => x.IdDesastre).HasDatabaseName("ix_evaluaciones_desastre");
        });

        // Bandeja de salida y contador del productor (AD-45, DA-04).
        modelo.ConfigurarBandejaSalida();
    }
}

/// <summary>Base de datos de la Consulta del Programa (modelo de lectura, AD-42). Es otra base: cada servicio es dueño de sus datos.</summary>
public sealed class ProgramaDbContext : DbContext
{
    public ProgramaDbContext(DbContextOptions<ProgramaDbContext> opciones) : base(opciones) { }

    public DbSet<FilaProgramaFila> Proyectos => Set<FilaProgramaFila>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<FilaProgramaFila>(e =>
        {
            e.ToTable("proyeccion_proyectos");
            e.HasKey(x => x.IdProyecto);
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto").ValueGeneratedNever();
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.Estado).HasColumnName("estado").IsRequired();
            e.Property(x => x.Presupuesto).HasColumnName("presupuesto").HasPrecision(18, 2);
            e.Property(x => x.CostoEjecutado).HasColumnName("costo_ejecutado").HasPrecision(18, 2);
            e.Property(x => x.Avance).HasColumnName("avance").HasPrecision(5, 2);
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.ActualizadoEn).HasColumnName("actualizado_en");
            e.HasIndex(x => x.IdDesastre).HasDatabaseName("ix_proyeccion_desastre");
        });

        // Eventos ya aplicados: consumo idempotente (AD-02).
        modelo.ConfigurarMensajesProcesados();
    }
}
