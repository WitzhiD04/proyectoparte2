using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NetTopologySuite.Geometries;
using Plataforma.Comun.Mensajeria;

namespace ConsultaDanos.Infraestructura;

// Proyecciones del modelo de lectura de daños (DA-08, AD-34). Se construyen solo con los eventos del bus y responden las
// consultas sin leer el almacén de Evaluación de Daños (AD-01). El esquema sale de este modelo (tipos, checks e índices);
// las filas se escriben con SQL explícito en AlmacenProyeccionesPostgis, que toma los bloqueos en un orden fijo.

/// <summary>p_evaluacion_vigente: un activo con el contenido de su evaluación vigente, su validación y su marca de fusión.</summary>
public sealed class FilaActivo
{
    public Guid IdActivo { get; set; }
    public Guid? IdEvaluacion { get; set; }
    public Guid? IdDesastre { get; set; }
    public string? TipoActivo { get; set; }
    public string? NivelDano { get; set; }
    public string? CodigoMunicipio { get; set; }
    public Point? Ubicacion { get; set; }
    public double? PrecisionM { get; set; }
    public string? EstadoEvaluacion { get; set; }
    public int VersionEvaluacion { get; set; }
    /// <summary>Versión del activo del contenido vigente; -1 si todavía no llegó contenido.</summary>
    public int VersionActivo { get; set; }
    /// <summary>OrigenContenido del dominio: desempata dos eventos con la misma versión del activo.</summary>
    public short OrigenContenido { get; set; }
    public string? EstadoValidacion { get; set; }
    public bool Validada { get; set; }
    public bool Fusionado { get; set; }
    public Guid? FusionadoEn { get; set; }
    public DateTimeOffset ActualizadoAl { get; set; }
}

/// <summary>p_validaciones: la validación vigente de cada evaluación (la de mayor versión, RF-ED-08).</summary>
public sealed class FilaValidacion
{
    public Guid IdEvaluacion { get; set; }
    public Guid IdActivo { get; set; }
    public string Estado { get; set; } = "";
    public int Version { get; set; }
    public string ValidadoPor { get; set; } = "";
    public DateTimeOffset InstanteValidacion { get; set; }
}

/// <summary>p_dano_por_zona: el consolidado de RF-ED-10 (desastre × municipio × tipo × nivel, solo validadas).</summary>
public sealed class FilaDanoZona
{
    public Guid IdDesastre { get; set; }
    public string CodigoMunicipio { get; set; } = "";
    public string TipoActivo { get; set; } = "";
    public string NivelDano { get; set; } = "";
    public int Cantidad { get; set; }
    public DateTimeOffset ActualizadoAl { get; set; }
}

/// <summary>p_historial_activo (RF-ED-15): solo inserción, como todo historial (AD-41).</summary>
public sealed class FilaHistorial
{
    public Guid IdActivo { get; set; }
    public Guid IdEvaluacion { get; set; }
    public int VersionEvaluacion { get; set; }
    public string Origen { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    public string NivelDano { get; set; } = "";
    public string TipoActivo { get; set; } = "";
    public string EstadoEvaluacion { get; set; } = "";
    public string? EstadoValidacion { get; set; }
    public bool EnConflicto { get; set; }
    public Guid? IdDesastre { get; set; }
    public string CodigoMunicipio { get; set; } = "";
    public string? Autor { get; set; }
    public string? Detalle { get; set; }
}

/// <summary>p_conflictos: lo que EV-10 y EV-22 dicen de cada conflicto, lleguen en el orden que lleguen.</summary>
public sealed class FilaConflicto
{
    public Guid IdConflicto { get; set; }
    public Guid? IdActivo { get; set; }
    public string? Tipo { get; set; }
    public Guid? IdEvaluacionEntrante { get; set; }
    public string Estado { get; set; } = "";
    public string? Decision { get; set; }
    public Guid? IdActivoVigente { get; set; }
    public DateTimeOffset? DetectadoEn { get; set; }
    public DateTimeOffset? ResueltoEn { get; set; }
}

/// <summary>p_bandeja: una sola fila con el tamaño de la bandeja y el conflicto pendiente más viejo (R-08).</summary>
public sealed class FilaBandeja
{
    public short Id { get; set; }
    public int Pendientes { get; set; }
    public DateTimeOffset? MasViejo { get; set; }
    public DateTimeOffset? DatoAl { get; set; }
}

/// <summary>p_desastres: el instante del último evento aplicado que cambió los datos de cada desastre (datoAl, AD-16).</summary>
public sealed class FilaDesastre
{
    public Guid IdDesastre { get; set; }
    public DateTimeOffset DatoAl { get; set; }
}

/// <summary>p_municipios: la capa de SE-2 (EV-21) con la geometría ya simplificada al proyectar (AD-17).</summary>
public sealed class FilaMunicipio
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? CodigoDepartamento { get; set; }
    public Geometry Geometria { get; set; } = null!;
    public int VersionCapa { get; set; }
    public DateTimeOffset ActualizadoAl { get; set; }
}

/// <summary>p_tipos_activo: los nombres del catálogo (EV-25).</summary>
public sealed class FilaTipoActivo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Version { get; set; }
}

/// <summary>p_integraciones: estado del cortacircuitos de cada adaptador (EV-23, R-10).</summary>
public sealed class FilaIntegracion
{
    public string Sistema { get; set; } = "";
    public string Estado { get; set; } = "";
    public DateTimeOffset Desde { get; set; }
    public long MensajesEnCola { get; set; }
}

public sealed class ConsultaDanosDbContext : DbContext
{
    private const string Niveles = "'SIN_DANO','LEVE','MODERADO','GRAVE','DESTRUCCION_TOTAL'";
    private const string Validaciones = "'VALIDADA','RECHAZADA','PENDIENTE'";

    public ConsultaDanosDbContext(DbContextOptions<ConsultaDanosDbContext> opciones) : base(opciones) { }

    /// <summary>Invalidaciones de la caché programadas en esta unidad de trabajo; se aplican al confirmar.</summary>
    public InvalidacionesPendientes Invalidaciones { get; } = new();

    public DbSet<FilaActivo> Activos => Set<FilaActivo>();
    public DbSet<FilaHistorial> Historial => Set<FilaHistorial>();
    public DbSet<FilaIntegracion> Integraciones => Set<FilaIntegracion>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.HasPostgresExtension("postgis");

        m.Entity<FilaActivo>(e =>
        {
            e.ToTable("p_evaluacion_vigente", t =>
            {
                t.HasCheckConstraint("ck_p_evaluacion_vigente_nivel", $"nivel_dano IS NULL OR nivel_dano IN ({Niveles})");
                t.HasCheckConstraint("ck_p_evaluacion_vigente_validacion", $"estado_validacion IS NULL OR estado_validacion IN ({Validaciones})");
                t.HasCheckConstraint("ck_p_evaluacion_vigente_version", "version_activo >= -1");
                t.HasCheckConstraint("ck_p_evaluacion_vigente_origen", "origen_contenido BETWEEN 0 AND 3");
            });
            e.HasKey(x => x.IdActivo);
            e.Property(x => x.IdActivo).HasColumnName("id_activo").ValueGeneratedNever();
            e.Property(x => x.IdEvaluacion).HasColumnName("id_evaluacion");
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo");
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano");
            e.Property(x => x.CodigoMunicipio).HasColumnName("codigo_municipio");
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion").HasColumnType("geometry(Point,4326)");
            e.Property(x => x.PrecisionM).HasColumnName("precision_m");
            e.Property(x => x.EstadoEvaluacion).HasColumnName("estado_evaluacion");
            e.Property(x => x.VersionEvaluacion).HasColumnName("version_evaluacion");
            e.Property(x => x.VersionActivo).HasColumnName("version_activo");
            e.Property(x => x.OrigenContenido).HasColumnName("origen_contenido");
            e.Property(x => x.EstadoValidacion).HasColumnName("estado_validacion");
            e.Property(x => x.Validada).HasColumnName("validada");
            e.Property(x => x.Fusionado).HasColumnName("fusionado");
            e.Property(x => x.FusionadoEn).HasColumnName("fusionado_en");
            e.Property(x => x.ActualizadoAl).HasColumnName("actualizado_al");
            // El mapa de un desastre se arma por este índice (QA-06): solo los activos con contenido que no se fusionaron.
            e.HasIndex(x => new { x.IdDesastre, x.CodigoMunicipio }).HasDatabaseName("ix_p_evaluacion_vigente_desastre")
                .HasFilter("version_activo >= 0 AND NOT fusionado");
        });

        m.Entity<FilaValidacion>(e =>
        {
            e.ToTable("p_validaciones", t => t.HasCheckConstraint("ck_p_validaciones_estado", $"estado IN ({Validaciones})"));
            e.HasKey(x => x.IdEvaluacion);
            e.Property(x => x.IdEvaluacion).HasColumnName("id_evaluacion").ValueGeneratedNever();
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.ValidadoPor).HasColumnName("validado_por");
            e.Property(x => x.InstanteValidacion).HasColumnName("instante_validacion");
        });

        m.Entity<FilaDanoZona>(e =>
        {
            e.ToTable("p_dano_por_zona", t =>
            {
                t.HasCheckConstraint("ck_p_dano_por_zona_cantidad", "cantidad >= 0");
                t.HasCheckConstraint("ck_p_dano_por_zona_nivel", $"nivel_dano IN ({Niveles})");
            });
            e.HasKey(x => new { x.IdDesastre, x.CodigoMunicipio, x.TipoActivo, x.NivelDano });
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.CodigoMunicipio).HasColumnName("codigo_municipio");
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo");
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano");
            e.Property(x => x.Cantidad).HasColumnName("cantidad");
            e.Property(x => x.ActualizadoAl).HasColumnName("actualizado_al");
        });

        m.Entity<FilaHistorial>(e =>
        {
            e.ToTable("p_historial_activo", t => t.HasCheckConstraint("ck_p_historial_activo_nivel", $"nivel_dano IN ({Niveles})"));
            e.HasKey(x => new { x.IdActivo, x.IdEvaluacion, x.VersionEvaluacion, x.Origen });
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.IdEvaluacion).HasColumnName("id_evaluacion");
            e.Property(x => x.VersionEvaluacion).HasColumnName("version_evaluacion");
            e.Property(x => x.Origen).HasColumnName("origen");
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.NivelDano).HasColumnName("nivel_dano");
            e.Property(x => x.TipoActivo).HasColumnName("tipo_activo");
            e.Property(x => x.EstadoEvaluacion).HasColumnName("estado_evaluacion");
            e.Property(x => x.EstadoValidacion).HasColumnName("estado_validacion");
            e.Property(x => x.EnConflicto).HasColumnName("en_conflicto");
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.CodigoMunicipio).HasColumnName("codigo_municipio");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.Property(x => x.Detalle).HasColumnName("detalle");
            e.HasIndex(x => new { x.IdActivo, x.Instante }).HasDatabaseName("ix_p_historial_activo_instante");
        });

        m.Entity<FilaConflicto>(e =>
        {
            e.ToTable("p_conflictos", t => t.HasCheckConstraint("ck_p_conflictos_estado", "estado IN ('PENDIENTE','RESUELTO')"));
            e.HasKey(x => x.IdConflicto);
            e.Property(x => x.IdConflicto).HasColumnName("id_conflicto").ValueGeneratedNever();
            e.Property(x => x.IdActivo).HasColumnName("id_activo");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.IdEvaluacionEntrante).HasColumnName("id_evaluacion_entrante");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Decision).HasColumnName("decision");
            e.Property(x => x.IdActivoVigente).HasColumnName("id_activo_vigente");
            e.Property(x => x.DetectadoEn).HasColumnName("detectado_en");
            e.Property(x => x.ResueltoEn).HasColumnName("resuelto_en");
            // Para la marca del mapa y para p_bandeja solo interesan los pendientes.
            e.HasIndex(x => x.IdActivo).HasDatabaseName("ix_p_conflictos_pendientes").HasFilter("estado = 'PENDIENTE'");
        });

        m.Entity<FilaBandeja>(e =>
        {
            e.ToTable("p_bandeja", t => t.HasCheckConstraint("ck_p_bandeja_unica", "id = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Pendientes).HasColumnName("pendientes");
            e.Property(x => x.MasViejo).HasColumnName("mas_viejo");
            e.Property(x => x.DatoAl).HasColumnName("dato_al");
            e.HasData(new FilaBandeja { Id = 1, Pendientes = 0 });
        });

        m.Entity<FilaDesastre>(e =>
        {
            e.ToTable("p_desastres");
            e.HasKey(x => x.IdDesastre);
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre").ValueGeneratedNever();
            e.Property(x => x.DatoAl).HasColumnName("dato_al");
        });

        m.Entity<FilaMunicipio>(e =>
        {
            e.ToTable("p_municipios");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.CodigoDepartamento).HasColumnName("codigo_departamento");
            e.Property(x => x.Geometria).HasColumnName("geometria").HasColumnType("geometry(Geometry,4326)");
            e.Property(x => x.VersionCapa).HasColumnName("version_capa");
            e.Property(x => x.ActualizadoAl).HasColumnName("actualizado_al");
        });

        m.Entity<FilaTipoActivo>(e =>
        {
            e.ToTable("p_tipos_activo");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Version).HasColumnName("version");
        });

        m.Entity<FilaIntegracion>(e =>
        {
            e.ToTable("p_integraciones");
            e.HasKey(x => x.Sistema);
            e.Property(x => x.Sistema).HasColumnName("sistema");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Desde).HasColumnName("desde");
            e.Property(x => x.MensajesEnCola).HasColumnName("mensajes_en_cola");
        });

        m.ConfigurarMensajesProcesados();
    }
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoConsultaDanos : IDesignTimeDbContextFactory<ConsultaDanosDbContext>
{
    public ConsultaDanosDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ConsultaDanosDbContext>()
            .UseNpgsql("Host=localhost;Database=consulta_danos", n => n.UseNetTopologySuite()).Options);
}
