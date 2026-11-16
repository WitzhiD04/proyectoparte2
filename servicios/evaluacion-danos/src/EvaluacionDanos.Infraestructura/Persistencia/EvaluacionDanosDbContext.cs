using System.Text;
using EvaluacionDanos.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetTopologySuite.Geometries;
using Plataforma.Comun.Mensajeria;

namespace EvaluacionDanos.Infraestructura.Persistencia;

/// <summary>Una división administrativa de la capa de SE-2, con su geometría en PostGIS (DA-07, DT-03).</summary>
public sealed class FilaDivision
{
    public string Capa { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? CodigoPadre { get; set; }
    public int VersionCapa { get; set; }
    public MultiPolygon Geometria { get; set; } = MultiPolygon.Empty;
}

/// <summary>Parte recibida de una versión de una capa (EV-21 llega por partes): dice cuándo la capa está completa.</summary>
public sealed class FilaParteCapa
{
    public string Capa { get; set; } = "";
    public int VersionCapa { get; set; }
    public int Parte { get; set; }
    public int Total { get; set; }
    public DateTime RecibidaEn { get; set; }
}

/// <summary>
/// Base propia de Evaluación de Daños (AD-01). El modelo declara tipos, largos, checks e índices: la migración se
/// genera desde aquí (DT-19). Columnas en snake_case, como el resto de la plataforma.
/// </summary>
public class EvaluacionDanosDbContext(DbContextOptions<EvaluacionDanosDbContext> opciones) : DbContext(opciones)
{
    public DbSet<TipoActivo> TiposActivo => Set<TipoActivo>();
    public DbSet<CriterioNivel> CriteriosNivel => Set<CriterioNivel>();
    public DbSet<Desastre> Desastres => Set<Desastre>();
    public DbSet<Activo> Activos => Set<Activo>();
    public DbSet<ActivoVersion> ActivoVersiones => Set<ActivoVersion>();
    public DbSet<Evaluacion> Evaluaciones => Set<Evaluacion>();
    public DbSet<EvaluacionVersion> EvaluacionVersiones => Set<EvaluacionVersion>();
    public DbSet<Evidencia> Evidencias => Set<Evidencia>();
    public DbSet<EvidenciaVinculada> EvidenciasVinculadas => Set<EvidenciaVinculada>();
    public DbSet<Conflicto> Conflictos => Set<Conflicto>();
    public DbSet<OperacionProcesada> OperacionesProcesadas => Set<OperacionProcesada>();
    public DbSet<FilaDivision> Divisiones => Set<FilaDivision>();
    public DbSet<FilaParteCapa> PartesCapa => Set<FilaParteCapa>();
    public DbSet<FilaBandeja> Bandeja => Set<FilaBandeja>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.HasPostgresExtension("postgis");
        // Bandeja de salida (AD-45) y registro de eventos ya consumidos (AD-02), en la misma base que los datos.
        modelo.ConfigurarBandejaSalida();
        modelo.ConfigurarMensajesProcesados();
        modelo.ApplyConfigurationsFromAssembly(typeof(EvaluacionDanosDbContext).Assembly);

        foreach (IMutableEntityType entidad in modelo.Model.GetEntityTypes())
        {
            if (entidad.ClrType.Assembly == typeof(FilaBandeja).Assembly)
            {
                continue;   // las tablas de mensajería ya declaran sus columnas
            }
            foreach (IMutableProperty propiedad in entidad.GetProperties())
            {
                propiedad.SetColumnName(SnakeCase(propiedad.Name));
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RechazarCambiosAlHistorial();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RechazarCambiosAlHistorial();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// EvaluacionVersion es de solo inserción: ninguna rama puede reescribir ni borrar el historial. Es la segunda
    /// barrera; la primera es el permiso de la base (el rol del servicio no tiene UPDATE ni DELETE, DT-02).
    /// </summary>
    private void RechazarCambiosAlHistorial()
    {
        foreach (var entrada in ChangeTracker.Entries<EvaluacionVersion>())
        {
            if (entrada.State == EntityState.Modified || entrada.State == EntityState.Deleted)
            {
                throw new HistorialInmutableException(
                    $"EvaluacionVersion {entrada.Entity.Id} es de solo inserción: no se permite {entrada.State}.");
            }
        }
    }

    internal static string SnakeCase(string nombre)
    {
        var texto = new StringBuilder(nombre.Length + 4);
        for (int i = 0; i < nombre.Length; i++)
        {
            char c = nombre[i];
            if (char.IsUpper(c) && i > 0)
            {
                texto.Append('_');
            }
            texto.Append(char.ToLowerInvariant(c));
        }
        return texto.ToString();
    }

    /// <summary>Check de una columna de texto que guarda una enumeración: la base no admite un valor fuera de ella.</summary>
    internal static void CheckDeEnum<TEntidad, TEnum>(EntityTypeBuilder<TEntidad> e, string tabla, string columna, bool admiteNulo = false)
        where TEntidad : class where TEnum : struct, Enum
    {
        string valores = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        string regla = $"{columna} IN ({valores})";
        e.ToTable(t => t.HasCheckConstraint($"ck_{tabla}_{columna}", admiteNulo ? $"{columna} IS NULL OR {regla}" : regla));
    }
}

public class HistorialInmutableException(string mensaje) : InvalidOperationException(mensaje);

// ---------- Mapeo (Fluent API) ----------
// Las enumeraciones se guardan como texto ("GRAVE") y no como número.

public class ConfiguracionTipoActivo : IEntityTypeConfiguration<TipoActivo>
{
    public void Configure(EntityTypeBuilder<TipoActivo> e)
    {
        e.ToTable("tipo_activo");
        e.HasKey(x => x.Codigo);
        e.Property(x => x.Codigo).HasMaxLength(Limites.CodigoTipo);
        e.Property(x => x.Nombre).IsRequired().HasMaxLength(CatalogoTipos.LargoNombre);
        // Dos administradores que cambian el mismo tipo a la vez: el segundo falla en vez de pisar al primero.
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasMany(x => x.Criterios).WithOne().HasForeignKey(c => c.TipoActivoCodigo);
    }
}

public class ConfiguracionCriterioNivel : IEntityTypeConfiguration<CriterioNivel>
{
    public void Configure(EntityTypeBuilder<CriterioNivel> e)
    {
        e.ToTable("criterio_nivel");
        e.HasKey(x => new { x.TipoActivoCodigo, x.Nivel });
        e.Property(x => x.Nivel).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Criterio).IsRequired().HasMaxLength(CatalogoTipos.LargoCriterio);
        EvaluacionDanosDbContext.CheckDeEnum<CriterioNivel, NivelDano>(e, "criterio_nivel", "nivel");
    }
}

public class ConfiguracionDesastre : IEntityTypeConfiguration<Desastre>
{
    public void Configure(EntityTypeBuilder<Desastre> e)
    {
        e.ToTable("desastre");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Nombre).IsRequired().HasMaxLength(Limites.Nombre);
        e.Property(x => x.Tipo).IsRequired().HasMaxLength(Limites.CodigoTipo);
        e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.CodigosMunicipio).HasColumnType("text[]");
        e.Property(x => x.Organismos).HasColumnType("text[]");
        e.Property(x => x.OrganismoResponsable).HasMaxLength(Limites.Nombre);
        e.Ignore(x => x.Cerrado);
        EvaluacionDanosDbContext.CheckDeEnum<Desastre, EstadoDesastre>(e, "desastre", "estado");
    }
}

public class ConfiguracionActivo : IEntityTypeConfiguration<Activo>
{
    public const string Ubicacion = "Ubicacion";

    public void Configure(EntityTypeBuilder<Activo> e)
    {
        e.ToTable("activo", t => t.HasCheckConstraint("ck_activo_codigo_municipio", "codigo_municipio ~ '^[0-9]{5}$'"));
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.TipoActivoCodigo).IsRequired().HasMaxLength(Limites.CodigoTipo);
        e.Property(x => x.Referencia).HasMaxLength(Limites.Referencia);
        e.Property(x => x.CreadoPorDispositivo).HasMaxLength(Limites.IdDispositivo);
        // El UPDATE lleva "WHERE version = <la leída>": si otro lote la cambió en paralelo, el guardado falla con
        // DbUpdateConcurrencyException y la operación se reevalúa (AD-43).
        e.Property(x => x.Version).IsConcurrencyToken();
        // Municipio DIVIPOLA asignado en el servidor (DA-07, E-12, corrige H-19).
        e.Property(x => x.CodigoMunicipio).IsRequired().HasMaxLength(5);
        // Ubicación como punto de PostGIS (DT-03), derivada de lat y lon: el dominio y el detector de duplicados
        // siguen trabajando con las coordenadas, y las consultas espaciales usan el punto con su índice.
        e.Property<Point>(Ubicacion).HasColumnType("geometry(Point,4326)")
            .HasComputedColumnSql("ST_SetSRID(ST_MakePoint(lon, lat), 4326)", stored: true);
        e.HasIndex(Ubicacion).HasMethod("gist");
        e.HasOne<TipoActivo>().WithMany().HasForeignKey(x => x.TipoActivoCodigo);
        e.HasOne<Activo>().WithMany().HasForeignKey(x => x.FusionadoEn);
        // El detector de duplicados busca por tipo dentro de un recuadro de coordenadas.
        e.HasIndex(x => new { x.TipoActivoCodigo, x.Lat, x.Lon });
        // La descarga filtra por municipio y por instante de cambio (DA-19).
        e.HasIndex(x => new { x.CodigoMunicipio, x.ActualizadoEn });
    }
}

public class ConfiguracionActivoVersion : IEntityTypeConfiguration<ActivoVersion>
{
    public void Configure(EntityTypeBuilder<ActivoVersion> e)
    {
        e.ToTable("activo_version");
        e.HasKey(x => new { x.ActivoId, x.Numero });
        e.Property(x => x.IdDispositivo).IsRequired().HasMaxLength(Limites.IdDispositivo);
        e.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId);
    }
}

public class ConfiguracionEvaluacion : IEntityTypeConfiguration<Evaluacion>
{
    public void Configure(EntityTypeBuilder<Evaluacion> e)
    {
        e.ToTable("evaluacion");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Evaluador).IsRequired().HasMaxLength(Limites.Nombre);
        e.Property(x => x.Organizacion).IsRequired().HasMaxLength(Limites.Nombre);
        e.Property(x => x.TipoActivoCodigo).IsRequired().HasMaxLength(Limites.CodigoTipo);
        e.Property(x => x.NivelDano).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Descripcion).HasMaxLength(Limites.Descripcion);
        e.Property(x => x.IdDispositivo).IsRequired().HasMaxLength(Limites.IdDispositivo);
        e.Property(x => x.ValidadoPor).HasMaxLength(Limites.Nombre);
        e.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId);
        e.HasOne<Desastre>().WithMany().HasForeignKey(x => x.DesastreId);
        e.HasOne<TipoActivo>().WithMany().HasForeignKey(x => x.TipoActivoCodigo);
        e.HasOne<Evaluacion>().WithMany().HasForeignKey(x => x.DuplicadoDe);
        e.HasIndex(x => x.IdDispositivo);
        e.HasIndex(x => x.ActivoId);
        e.HasIndex(x => x.DesastreId);
        EvaluacionDanosDbContext.CheckDeEnum<Evaluacion, NivelDano>(e, "evaluacion", "nivel_dano");
        EvaluacionDanosDbContext.CheckDeEnum<Evaluacion, EstadoEvaluacion>(e, "evaluacion", "estado");
    }
}

public class ConfiguracionEvaluacionVersion : IEntityTypeConfiguration<EvaluacionVersion>
{
    public void Configure(EntityTypeBuilder<EvaluacionVersion> e)
    {
        e.ToTable("evaluacion_version");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.ContenidoJson).HasColumnType("jsonb");
        e.Property(x => x.Autor).IsRequired().HasMaxLength(Limites.Nombre);
        e.Property(x => x.IdDispositivo).IsRequired().HasMaxLength(Limites.IdDispositivo);
        e.Property(x => x.Motivo).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Detalle).HasColumnType("text");
        e.HasOne<Evaluacion>().WithMany().HasForeignKey(x => x.EvaluacionId);
        e.HasIndex(x => new { x.EvaluacionId, x.NumeroVersion }).IsUnique();
        EvaluacionDanosDbContext.CheckDeEnum<EvaluacionVersion, MotivoVersion>(e, "evaluacion_version", "motivo");
    }
}

public class ConfiguracionEvidencia : IEntityTypeConfiguration<Evidencia>
{
    public void Configure(EntityTypeBuilder<Evidencia> e)
    {
        e.ToTable("evidencia");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Sha256Declarado).IsRequired().HasMaxLength(64);
        e.Property(x => x.ClaveObjeto).HasMaxLength(200);
        e.HasOne<Evaluacion>().WithMany().HasForeignKey(x => x.EvaluacionId);
        e.HasIndex(x => x.EvaluacionId);
    }
}

public class ConfiguracionEvidenciaVinculada : IEntityTypeConfiguration<EvidenciaVinculada>
{
    public void Configure(EntityTypeBuilder<EvidenciaVinculada> e)
    {
        e.ToTable("evidencia_vinculada");
        e.HasKey(x => new { x.EvaluacionId, x.EvidenciaId });
        e.HasOne<Evaluacion>().WithMany().HasForeignKey(x => x.EvaluacionId);
        e.HasOne<Evidencia>().WithMany().HasForeignKey(x => x.EvidenciaId);
        e.HasOne<Conflicto>().WithMany().HasForeignKey(x => x.ConflictoId);
    }
}

public class ConfiguracionConflicto : IEntityTypeConfiguration<Conflicto>
{
    public void Configure(EntityTypeBuilder<Conflicto> e)
    {
        e.ToTable("conflicto");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
        // Token de concurrencia: dos operadores que resuelven el mismo conflicto a la vez no deciden dos veces.
        e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
        e.Property(x => x.EstadoPrevioEntrante).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.DecididoPor).HasMaxLength(Limites.Nombre);
        e.Property(x => x.Justificacion).HasMaxLength(Limites.Descripcion);
        // text: la regla nombra a todos los dispositivos que cambiaron el activo y no tiene tope.
        e.Property(x => x.Regla).IsRequired().HasColumnType("text");
        e.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId);
        e.HasOne<Evaluacion>().WithMany().HasForeignKey(x => x.EvaluacionVigenteId);
        e.HasOne<Evaluacion>().WithMany().HasForeignKey(x => x.EvaluacionEntranteId);
        e.HasOne<EvaluacionVersion>().WithMany().HasForeignKey(x => x.VersionEntranteId);
        e.HasIndex(x => x.Estado);
        e.HasIndex(x => x.EvaluacionEntranteId);
        EvaluacionDanosDbContext.CheckDeEnum<Conflicto, TipoConflicto>(e, "conflicto", "tipo");
        EvaluacionDanosDbContext.CheckDeEnum<Conflicto, EstadoConflicto>(e, "conflicto", "estado");
        EvaluacionDanosDbContext.CheckDeEnum<Conflicto, DecisionConflicto>(e, "conflicto", "decision", admiteNulo: true);
    }
}

public class ConfiguracionOperacionProcesada : IEntityTypeConfiguration<OperacionProcesada>
{
    public void Configure(EntityTypeBuilder<OperacionProcesada> e)
    {
        e.ToTable("operacion_procesada");
        e.HasKey(x => x.IdOperacion);
        e.Property(x => x.IdOperacion).ValueGeneratedNever();
        e.Property(x => x.IdDispositivo).IsRequired().HasMaxLength(Limites.IdDispositivo);
        e.Property(x => x.ResultadoJson).HasColumnType("jsonb");
    }
}

public class ConfiguracionDivision : IEntityTypeConfiguration<FilaDivision>
{
    public void Configure(EntityTypeBuilder<FilaDivision> e)
    {
        e.ToTable("division_administrativa");
        e.HasKey(x => new { x.Capa, x.Codigo });
        e.Property(x => x.Capa).HasMaxLength(20);
        e.Property(x => x.Codigo).HasMaxLength(5);
        e.Property(x => x.Nombre).IsRequired().HasMaxLength(Limites.Nombre);
        e.Property(x => x.CodigoPadre).HasMaxLength(2);
        e.Property(x => x.Geometria).HasColumnType("geometry(MultiPolygon,4326)");
        // Punto en polígono (ST_Contains) y vecino más cercano (operador de distancia) usan este índice.
        e.HasIndex(x => x.Geometria).HasMethod("gist");
    }
}

public class ConfiguracionParteCapa : IEntityTypeConfiguration<FilaParteCapa>
{
    public void Configure(EntityTypeBuilder<FilaParteCapa> e)
    {
        e.ToTable("capa_parte");
        e.HasKey(x => new { x.Capa, x.VersionCapa, x.Parte });
        e.Property(x => x.Capa).HasMaxLength(20);
    }
}
