using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Plataforma.Comun.Mensajeria;

namespace GestionDesastres.Infraestructura.Persistencia;

// Modelo de persistencia: filas planas. Los repositorios convierten entre estas filas y las entidades del dominio.

/// <summary>Atributos del registro, que no cambian, más la versión (testigo de concurrencia) y la última modificación.</summary>
public sealed class DesastreFila
{
    public Guid Id { get; set; }
    public string CodigoTipo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public DateTimeOffset InstanteInicio { get; set; }
    public Point Ubicacion { get; set; } = null!;
    public string[] OrganismosRegistra { get; set; } = [];
    public string[] Fuentes { get; set; } = [];
    public int Version { get; set; }
    public DateTimeOffset InstanteActualizacion { get; set; }
}

/// <summary>Una entrada del historial versionado con el estado resultante (AD-20). Solo inserción.</summary>
public sealed class HistorialFila
{
    public Guid IdDesastre { get; set; }
    public int Version { get; set; }
    public string TipoCambio { get; set; } = "";
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string Autor { get; set; } = "";
    public string? Organizacion { get; set; }
    public string Justificacion { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    /// <summary>EstadoResultante en JSON: severidad, estado, perfil, responsable y participantes.</summary>
    public string Resultado { get; set; } = "{}";
}

/// <summary>Una versión de la delimitación (RN-4, Cuadro 19). Solo inserción.</summary>
public sealed class ZonaFila
{
    public Guid IdDesastre { get; set; }
    public int Version { get; set; }
    public DateTimeOffset VigenteDesde { get; set; }
    public Geometry? Geometria { get; set; }
    public string[] CodigosMunicipio { get; set; } = [];
    public string Fuente { get; set; } = "";
    public string Autor { get; set; } = "";
    public string Justificacion { get; set; } = "";
}

public sealed class TipoDesastreFila
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string CodigoPerfil { get; set; } = "";
    public string Criterios { get; set; } = "{}";
    public string Organismos { get; set; } = "{}";
    public DateTimeOffset VigenteDesde { get; set; }
    public DateTimeOffset? VigenteHasta { get; set; }
}

public sealed class PerfilFila
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Rango { get; set; }
    public string[] Estados { get; set; } = [];
}

public sealed class PerfilPorNivelFila
{
    public string Nivel { get; set; } = "";
    public string CodigoPerfil { get; set; } = "";
}

/// <summary>Copia local de los proyectos de reconstrucción (E-07).</summary>
public sealed class ProyectoFila
{
    public Guid IdProyecto { get; set; }
    public Guid IdDesastre { get; set; }
    public bool Final { get; set; }
    public int Version { get; set; }
}

/// <summary>Copia local de la capa de municipios de SE-2 (EV-21).</summary>
public sealed class MunicipioFila
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? CodigoDepartamento { get; set; }
    public Geometry Geometria { get; set; } = null!;
    public int VersionCapa { get; set; }
}

/// <summary>
/// Base de Gestión de Desastres. El modelo describe el esquema completo y las migraciones lo crean (DT-19); los permisos de
/// mínimo privilegio que hacen el historial y las zonas de solo inserción (AD-20, RN-4) están en la migración.
/// </summary>
public sealed class GestionDesastresDbContext : DbContext
{
    public GestionDesastresDbContext(DbContextOptions<GestionDesastresDbContext> opciones) : base(opciones) { }

    public DbSet<DesastreFila> Desastres => Set<DesastreFila>();
    public DbSet<HistorialFila> Historial => Set<HistorialFila>();
    public DbSet<ZonaFila> Zonas => Set<ZonaFila>();
    public DbSet<TipoDesastreFila> Tipos => Set<TipoDesastreFila>();
    public DbSet<PerfilFila> Perfiles => Set<PerfilFila>();
    public DbSet<PerfilPorNivelFila> PerfilPorNivel => Set<PerfilPorNivelFila>();
    public DbSet<ProyectoFila> Proyectos => Set<ProyectoFila>();
    public DbSet<MunicipioFila> Municipios => Set<MunicipioFila>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.HasPostgresExtension("postgis");

        modelo.Entity<DesastreFila>(e =>
        {
            e.ToTable("desastres");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CodigoTipo).HasColumnName("codigo_tipo").IsRequired();
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.Descripcion).HasColumnName("descripcion").IsRequired();
            e.Property(x => x.InstanteInicio).HasColumnName("instante_inicio");
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion").HasColumnType("geometry(Point,4326)").IsRequired();
            e.Property(x => x.OrganismosRegistra).HasColumnName("organismos_registra").IsRequired();
            e.Property(x => x.Fuentes).HasColumnName("fuentes").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.Property(x => x.InstanteActualizacion).HasColumnName("instante_actualizacion");
            e.HasOne<TipoDesastreFila>().WithMany().HasForeignKey(x => x.CodigoTipo).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<HistorialFila>(e =>
        {
            e.ToTable("historial", t => t.HasCheckConstraint("ck_historial_tipo",
                "tipo_cambio IN ('Registro', 'Severidad', 'Estado', 'Zona', 'Organismos', 'Perfil')"));
            e.HasKey(x => new { x.IdDesastre, x.Version });
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.TipoCambio).HasColumnName("tipo_cambio").IsRequired();
            e.Property(x => x.ValorAnterior).HasColumnName("valor_anterior");
            e.Property(x => x.ValorNuevo).HasColumnName("valor_nuevo");
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Organizacion).HasColumnName("organizacion");
            e.Property(x => x.Justificacion).HasColumnName("justificacion").IsRequired();
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Resultado).HasColumnName("resultado").HasColumnType("jsonb").IsRequired();
            e.HasOne<DesastreFila>().WithMany().HasForeignKey(x => x.IdDesastre);
            e.HasIndex(x => new { x.IdDesastre, x.Instante }).HasDatabaseName("ix_historial_desastre_instante");
        });

        modelo.Entity<ZonaFila>(e =>
        {
            e.ToTable("zonas", t => t.HasCheckConstraint("ck_zonas_contenido", "geometria IS NOT NULL OR cardinality(codigos_municipio) > 0"));
            e.HasKey(x => new { x.IdDesastre, x.Version });
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.VigenteDesde).HasColumnName("vigente_desde");
            e.Property(x => x.Geometria).HasColumnName("geometria").HasColumnType("geometry(Geometry,4326)");
            e.Property(x => x.CodigosMunicipio).HasColumnName("codigos_municipio").IsRequired();
            e.Property(x => x.Fuente).HasColumnName("fuente").IsRequired();
            e.Property(x => x.Autor).HasColumnName("autor").IsRequired();
            e.Property(x => x.Justificacion).HasColumnName("justificacion").IsRequired();
            e.HasOne<DesastreFila>().WithMany().HasForeignKey(x => x.IdDesastre);
            e.HasIndex(x => x.Geometria).HasMethod("gist").HasDatabaseName("ix_zonas_geometria");
        });

        modelo.Entity<TipoDesastreFila>(e =>
        {
            e.ToTable("tipos_desastre");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.CodigoPerfil).HasColumnName("codigo_perfil").IsRequired();
            e.Property(x => x.Criterios).HasColumnName("criterios").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Organismos).HasColumnName("organismos").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.VigenteDesde).HasColumnName("vigente_desde");
            e.Property(x => x.VigenteHasta).HasColumnName("vigente_hasta");
            e.HasOne<PerfilFila>().WithMany().HasForeignKey(x => x.CodigoPerfil).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<PerfilFila>(e =>
        {
            e.ToTable("perfiles_ciclo_vida");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.Rango).HasColumnName("rango");
            e.Property(x => x.Estados).HasColumnName("estados").IsRequired();
        });

        modelo.Entity<PerfilPorNivelFila>(e =>
        {
            e.ToTable("perfil_por_nivel", t => t.HasCheckConstraint("ck_perfil_por_nivel_nivel", "nivel IN ('Nivel1', 'Nivel2', 'Nivel3', 'Nivel4')"));
            e.HasKey(x => x.Nivel);
            e.Property(x => x.Nivel).HasColumnName("nivel");
            e.Property(x => x.CodigoPerfil).HasColumnName("codigo_perfil").IsRequired();
            e.HasOne<PerfilFila>().WithMany().HasForeignKey(x => x.CodigoPerfil).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<ProyectoFila>(e =>
        {
            e.ToTable("proyectos_desastre");
            e.HasKey(x => x.IdProyecto);
            e.Property(x => x.IdProyecto).HasColumnName("id_proyecto").ValueGeneratedNever();
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Final).HasColumnName("final");
            e.Property(x => x.Version).HasColumnName("version");
            e.HasIndex(x => x.IdDesastre).HasDatabaseName("ix_proyectos_desastre");
        });

        modelo.Entity<MunicipioFila>(e =>
        {
            e.ToTable("municipios");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(x => x.CodigoDepartamento).HasColumnName("codigo_departamento");
            e.Property(x => x.Geometria).HasColumnName("geometria").HasColumnType("geometry(Geometry,4326)").IsRequired();
            e.Property(x => x.VersionCapa).HasColumnName("version_capa");
            e.HasIndex(x => x.Geometria).HasMethod("gist").HasDatabaseName("ix_municipios_geometria");
        });

        modelo.ConfigurarBandejaSalida();
        modelo.ConfigurarMensajesProcesados();
    }
}
