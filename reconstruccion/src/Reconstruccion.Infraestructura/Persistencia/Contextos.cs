using Microsoft.EntityFrameworkCore;

namespace Reconstruccion.Infraestructura.Persistencia;

/// <summary>
/// Base de datos del servicio de Reconstrucción. Las tablas las crea db/01-reconstruccion.sql;
/// aquí solo se dice a qué tabla y columna corresponde cada propiedad.
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
    public DbSet<MensajeSalidaFila> BandejaSalida => Set<MensajeSalidaFila>();
    public DbSet<DesastreLocalFila> Desastres => Set<DesastreLocalFila>();
    public DbSet<EvaluacionValidadaFila> Evaluaciones => Set<EvaluacionValidadaFila>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<ProyectoFila>(e =>
        {
            e.ToTable("proyectos");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(p => p.IdDesastre).HasColumnName("id_desastre");
            e.Property(p => p.IdActivo).HasColumnName("id_activo");
            e.Property(p => p.Nombre).HasColumnName("nombre");
            e.Property(p => p.Alcance).HasColumnName("alcance");
            e.Property(p => p.Estado).HasColumnName("estado");
            e.Property(p => p.Presupuesto).HasColumnName("presupuesto");
            e.Property(p => p.FuenteFinanciacion).HasColumnName("fuente_financiacion");
            e.Property(p => p.Responsable).HasColumnName("responsable");
            e.Property(p => p.Contratista).HasColumnName("contratista");
            e.Property(p => p.Inicio).HasColumnName("inicio");
            e.Property(p => p.Fin).HasColumnName("fin");
            e.Property(p => p.Avance).HasColumnName("avance");
            e.Property(p => p.CostoEjecutado).HasColumnName("costo_ejecutado");
            // Si dos usuarios cambian el mismo proyecto a la vez, el segundo guardado falla en vez de pisar al primero.
            e.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasMany(p => p.Evaluaciones).WithOne().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<EvaluacionOrigenFila>(e =>
        {
            e.ToTable("evaluaciones_origen");
            e.HasKey(x => new { x.IdProyecto, x.IdEvaluacion });
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.IdEvaluacion).HasColumnName("id_evaluacion");
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo");
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano");
            e.Property(x => x.Vigente).HasColumnName("vigente");
        });

        modelo.Entity<EntradaHistorialFila>(e =>
        {
            e.ToTable("historial");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.TipoCambio).HasColumnName("tipo_cambio");
            e.Property(x => x.ValorAnterior).HasColumnName("valor_anterior");
            e.Property(x => x.ValorNuevo).HasColumnName("valor_nuevo");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.Property(x => x.Justificacion).HasColumnName("justificacion");
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Procedencia).HasColumnName("procedencia");
            // Llave foránea a proyectos (como en el SQL): así EF inserta el proyecto antes que sus registros.
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<RegistroAvanceFila>(e =>
        {
            e.ToTable("registros_avance");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Porcentaje).HasColumnName("porcentaje");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Procedencia).HasColumnName("procedencia");
            // Llave foránea a proyectos (como en el SQL): así EF inserta el proyecto antes que sus registros.
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<RegistroCostoFila>(e =>
        {
            e.ToTable("registros_costo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Monto).HasColumnName("monto");
            e.Property(x => x.Concepto).HasColumnName("concepto");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.Property(x => x.Instante).HasColumnName("instante");
            // Llave foránea a proyectos (como en el SQL): así EF inserta el proyecto antes que sus registros.
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<DocumentoFila>(e =>
        {
            e.ToTable("documentos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion");
            e.Property(x => x.Hash).HasColumnName("hash");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.Property(x => x.Instante).HasColumnName("instante");
            // Llave foránea a proyectos (como en el SQL): así EF inserta el proyecto antes que sus registros.
            e.HasOne<ProyectoFila>().WithMany().HasForeignKey(x => x.IdProyecto);
        });

        modelo.Entity<LoteCreacionFila>(e =>
        {
            e.ToTable("lotes_creacion");
            e.HasKey(x => x.ClaveIdempotencia);
            e.Property(x => x.ClaveIdempotencia).HasColumnName("clave_idempotencia");
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.IdsProyectos).HasColumnName("ids_proyectos");
        });

        modelo.Entity<MensajeSalidaFila>(e =>
        {
            e.ToTable("bandeja_salida");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Secuencia).HasColumnName("secuencia").ValueGeneratedOnAdd();
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.ClaveIdempotencia).HasColumnName("clave_idempotencia");
            e.Property(x => x.Contenido).HasColumnName("contenido");
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.PublicadoEn).HasColumnName("publicado_en");
        });

        modelo.Entity<DesastreLocalFila>(e =>
        {
            e.ToTable("desastres_locales");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Estado).HasColumnName("estado");
        });

        modelo.Entity<EvaluacionValidadaFila>(e =>
        {
            e.ToTable("evaluaciones_validadas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.NombreActivo).HasColumnName("nombre_activo");
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo");
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano");
            e.Property(x => x.Municipio).HasColumnName("municipio");
            e.Property(x => x.Estado).HasColumnName("estado");
        });
    }
}

/// <summary>Base de datos de la Consulta del Programa (modelo de lectura). La crea db/02-programa.sql.</summary>
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
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Presupuesto).HasColumnName("presupuesto");
            e.Property(x => x.CostoEjecutado).HasColumnName("costo_ejecutado");
            e.Property(x => x.Avance).HasColumnName("avance");
            e.Property(x => x.Version).HasColumnName("version");
        });
    }
}
