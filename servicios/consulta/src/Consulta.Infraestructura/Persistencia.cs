using System.Text.Json;
using Consulta.Aplicacion;
using Consulta.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NetTopologySuite.Geometries;
using Plataforma.Comun.Mensajeria;

namespace Consulta.Infraestructura;

// Proyecciones del modelo de lectura (AD-15). Se construyen con los eventos del bus y responden la consulta sin cálculo
// adicional. Con dos réplicas consumiendo la misma cola los eventos pueden llegar en otro orden; por eso cada aspecto del
// desastre (registro, nivel, estado, zona, organismos) tiene su propia versión y solo una más nueva lo cambia.

public sealed class DesastreLeidoFila
{
    public Guid Id { get; set; }
    public bool Registrado { get; set; }
    public string? Tipo { get; set; }
    public string? Nombre { get; set; }
    public string? Descripcion { get; set; }
    public DateTimeOffset? InstanteInicio { get; set; }
    public Point? Ubicacion { get; set; }
    public string? OrganismoRegistra { get; set; }
    public string? Severidad { get; set; }
    public int SeveridadVersion { get; set; }
    public DateTimeOffset? SeveridadAl { get; set; }
    public string? Estado { get; set; }
    public int EstadoVersion { get; set; }
    public DateTimeOffset? EstadoAl { get; set; }
    public string? Responsable { get; set; }
    public string Participantes { get; set; } = "[]";
    public int OrganismosVersion { get; set; }
    public DateTimeOffset? OrganismosAl { get; set; }
    public int ZonaVersion { get; set; }
    public DateTimeOffset? ZonaAl { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}

public sealed class ZonaLeidaFila
{
    public Guid IdDesastre { get; set; }
    public int Version { get; set; }
    public DateTimeOffset VigenteDesde { get; set; }
    public Geometry? Geometria { get; set; }
    public Geometry? GeometriaSimplificada { get; set; }
    public string[] CodigosMunicipio { get; set; } = [];
    public string Fuente { get; set; } = "";
}

public sealed class EvolucionFila
{
    public Guid IdDesastre { get; set; }
    public string Tipo { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset Instante { get; set; }
    public string? Anterior { get; set; }
    public string? Nuevo { get; set; }
    public string Autor { get; set; } = "";
}

public sealed class MunicipioLeidoFila
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? CodigoDepartamento { get; set; }
    public Geometry Geometria { get; set; } = null!;
    public int VersionCapa { get; set; }
}

public sealed class IntegracionFila
{
    public string Sistema { get; set; } = "";
    public string Estado { get; set; } = "";
    public DateTimeOffset Desde { get; set; }
    public long MensajesEnCola { get; set; }
}

public sealed class ConsultaDbContext : DbContext
{
    public ConsultaDbContext(DbContextOptions<ConsultaDbContext> opciones) : base(opciones) { }

    public DbSet<DesastreLeidoFila> Desastres => Set<DesastreLeidoFila>();
    public DbSet<ZonaLeidaFila> Zonas => Set<ZonaLeidaFila>();
    public DbSet<EvolucionFila> Evolucion => Set<EvolucionFila>();
    public DbSet<MunicipioLeidoFila> Municipios => Set<MunicipioLeidoFila>();
    public DbSet<IntegracionFila> Integraciones => Set<IntegracionFila>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.HasPostgresExtension("postgis");
        m.Entity<DesastreLeidoFila>(e =>
        {
            e.ToTable("p_desastres");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Registrado).HasColumnName("registrado");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.InstanteInicio).HasColumnName("instante_inicio");
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion").HasColumnType("geometry(Point,4326)");
            e.Property(x => x.OrganismoRegistra).HasColumnName("organismo_registra");
            e.Property(x => x.Severidad).HasColumnName("severidad");
            e.Property(x => x.SeveridadVersion).HasColumnName("severidad_version");
            e.Property(x => x.SeveridadAl).HasColumnName("severidad_al");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.EstadoVersion).HasColumnName("estado_version");
            e.Property(x => x.EstadoAl).HasColumnName("estado_al");
            e.Property(x => x.Responsable).HasColumnName("responsable");
            e.Property(x => x.Participantes).HasColumnName("participantes").HasColumnType("jsonb");
            e.Property(x => x.OrganismosVersion).HasColumnName("organismos_version");
            e.Property(x => x.OrganismosAl).HasColumnName("organismos_al");
            e.Property(x => x.ZonaVersion).HasColumnName("zona_version");
            e.Property(x => x.ZonaAl).HasColumnName("zona_al");
            e.Property(x => x.ActualizadoEn).HasColumnName("actualizado_en");
            e.HasIndex(x => x.Estado).HasDatabaseName("ix_p_desastres_estado");
        });
        m.Entity<ZonaLeidaFila>(e =>
        {
            e.ToTable("p_zonas");
            e.HasKey(x => new { x.IdDesastre, x.Version });
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.VigenteDesde).HasColumnName("vigente_desde");
            e.Property(x => x.Geometria).HasColumnName("geometria").HasColumnType("geometry(Geometry,4326)");
            e.Property(x => x.GeometriaSimplificada).HasColumnName("geometria_simplificada").HasColumnType("geometry(Geometry,4326)");
            e.Property(x => x.CodigosMunicipio).HasColumnName("codigos_municipio");
            e.Property(x => x.Fuente).HasColumnName("fuente");
        });
        m.Entity<EvolucionFila>(e =>
        {
            e.ToTable("p_evolucion");
            e.HasKey(x => new { x.IdDesastre, x.Tipo, x.Version });
            e.Property(x => x.IdDesastre).HasColumnName("id_desastre");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Instante).HasColumnName("instante");
            e.Property(x => x.Anterior).HasColumnName("anterior");
            e.Property(x => x.Nuevo).HasColumnName("nuevo");
            e.Property(x => x.Autor).HasColumnName("autor");
            e.HasIndex(x => new { x.IdDesastre, x.Instante }).HasDatabaseName("ix_p_evolucion_desastre_instante");
        });
        m.Entity<MunicipioLeidoFila>(e =>
        {
            e.ToTable("p_municipios");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.CodigoDepartamento).HasColumnName("codigo_departamento");
            e.Property(x => x.Geometria).HasColumnName("geometria").HasColumnType("geometry(Geometry,4326)");
            e.Property(x => x.VersionCapa).HasColumnName("version_capa");
        });
        m.Entity<IntegracionFila>(e =>
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

/// <summary>Las proyecciones, con SQL de inserción o actualización protegido por versión (idempotente y tolerante al desorden).</summary>
public sealed class ProyeccionesPostgis : IProyecciones
{
    /// <summary>Tolerancia de simplificación de la geometría para el mapa, en grados (alrededor de 50 m).</summary>
    public const double Tolerancia = 0.0005;
    private readonly ConsultaDbContext _db;
    public ProyeccionesPostgis(ConsultaDbContext db) { _db = db; }

    public async Task AplicarAsync(DesastreRegistrado h, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO p_desastres (id, registrado, tipo, nombre, descripcion, instante_inicio, ubicacion, organismo_registra,
                severidad, severidad_version, severidad_al, estado, estado_version, estado_al, responsable, participantes,
                organismos_version, zona_version, actualizado_en)
            VALUES ({h.Id}, true, {h.Tipo}, {h.Nombre}, {h.Descripcion}, {h.InstanteInicio}, ST_SetSRID(ST_MakePoint({h.Lon}, {h.Lat}), 4326),
                {h.OrganismoRegistra}, {h.Severidad}, {h.Version}, {h.Instante}, {h.Estado}, {h.Version}, {h.Instante}, {h.Responsable}, '[]',
                0, 0, {h.Instante})
            ON CONFLICT (id) DO UPDATE SET registrado = true, tipo = EXCLUDED.tipo, nombre = EXCLUDED.nombre, descripcion = EXCLUDED.descripcion,
                instante_inicio = EXCLUDED.instante_inicio, ubicacion = EXCLUDED.ubicacion, organismo_registra = EXCLUDED.organismo_registra,
                severidad = CASE WHEN p_desastres.severidad_version < EXCLUDED.severidad_version THEN EXCLUDED.severidad ELSE p_desastres.severidad END,
                severidad_version = GREATEST(p_desastres.severidad_version, EXCLUDED.severidad_version),
                severidad_al = CASE WHEN p_desastres.severidad_version < EXCLUDED.severidad_version THEN EXCLUDED.severidad_al ELSE p_desastres.severidad_al END,
                estado = CASE WHEN p_desastres.estado_version < EXCLUDED.estado_version THEN EXCLUDED.estado ELSE p_desastres.estado END,
                estado_version = GREATEST(p_desastres.estado_version, EXCLUDED.estado_version),
                estado_al = CASE WHEN p_desastres.estado_version < EXCLUDED.estado_version THEN EXCLUDED.estado_al ELSE p_desastres.estado_al END,
                responsable = COALESCE(p_desastres.responsable, EXCLUDED.responsable),
                actualizado_en = GREATEST(p_desastres.actualizado_en, EXCLUDED.actualizado_en)", ct);
        await EvolucionAsync(h.Id, "REGISTRO", h.Version, h.Instante, null, $"{h.Severidad}|{h.Estado}", h.Autor, ct);
    }

    public async Task AplicarAsync(SeveridadCambiada h, CancellationToken ct)
    {
        await AsegurarFilaAsync(h.Id, h.Instante, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE p_desastres SET severidad = {h.Nueva}, severidad_version = {h.Version}, severidad_al = {h.Instante},
                actualizado_en = GREATEST(actualizado_en, {h.Instante})
            WHERE id = {h.Id} AND severidad_version < {h.Version}", ct);
        await EvolucionAsync(h.Id, "SEVERIDAD", h.Version, h.Instante, h.Anterior, h.Nueva, h.Autor, ct);
    }

    public async Task AplicarAsync(EstadoCambiado h, CancellationToken ct)
    {
        await AsegurarFilaAsync(h.Id, h.Instante, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE p_desastres SET estado = {h.Nuevo}, estado_version = {h.Version}, estado_al = {h.Instante},
                actualizado_en = GREATEST(actualizado_en, {h.Instante})
            WHERE id = {h.Id} AND estado_version < {h.Version}", ct);
        await EvolucionAsync(h.Id, "ESTADO", h.Version, h.Instante, h.Anterior, h.Nuevo, h.Autor, ct);
    }

    public async Task AplicarAsync(ZonaActualizada h, CancellationToken ct)
    {
        await AsegurarFilaAsync(h.Id, h.Instante, ct);
        string[] municipios = h.CodigosMunicipio.ToArray();
        // La geometría simplificada se calcula una vez, al proyectar, y no en cada consulta (AD-17). Si el evento solo trae
        // municipios, la geometría es su unión, tomada de la capa de SE-2.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            WITH g AS (
                SELECT COALESCE(
                    ST_SetSRID(ST_GeomFromGeoJSON({h.GeometriaGeoJson}::text), 4326),
                    (SELECT ST_Union(geometria) FROM p_municipios WHERE codigo = ANY({municipios}))) AS geometria)
            INSERT INTO p_zonas (id_desastre, version, vigente_desde, geometria, geometria_simplificada, codigos_municipio, fuente)
            SELECT {h.Id}, {h.Version}, {h.Instante}, g.geometria, ST_SimplifyPreserveTopology(g.geometria, {Tolerancia}), {municipios}, {h.Fuente}
            FROM g
            ON CONFLICT (id_desastre, version) DO NOTHING", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE p_desastres SET zona_version = {h.Version}, zona_al = {h.Instante}, actualizado_en = GREATEST(actualizado_en, {h.Instante})
            WHERE id = {h.Id} AND zona_version < {h.Version}", ct);
        await EvolucionAsync(h.Id, "ZONA", h.Version, h.Instante, h.Version > 1 ? $"v{h.Version - 1}" : null, $"v{h.Version}", h.Autor, ct);
    }

    public async Task AplicarAsync(OrganismosCambiados h, CancellationToken ct)
    {
        await AsegurarFilaAsync(h.Id, h.Instante, ct);
        string participantes = JsonSerializer.Serialize(h.Participantes.Select(p => new { organismo = p.Organismo, responsabilidad = p.Responsabilidad }));
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE p_desastres SET responsable = {h.Responsable}, participantes = {participantes}::jsonb, organismos_version = {h.Version},
                organismos_al = {h.Instante}, actualizado_en = GREATEST(actualizado_en, {h.Instante})
            WHERE id = {h.Id} AND organismos_version < {h.Version}", ct);
        await EvolucionAsync(h.Id, "ORGANISMOS", h.Version, h.Instante, null, h.Responsable, h.Autor, ct);
    }

    public Task AplicarAsync(EstadoIntegracion h, CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO p_integraciones (sistema, estado, desde, mensajes_en_cola) VALUES ({h.Sistema}, {h.Nuevo}, {h.Instante}, {h.MensajesEnCola})
        ON CONFLICT (sistema) DO UPDATE SET estado = EXCLUDED.estado, desde = EXCLUDED.desde, mensajes_en_cola = EXCLUDED.mensajes_en_cola
        WHERE p_integraciones.desde <= EXCLUDED.desde", ct);

    public async Task AplicarCapaAsync(IReadOnlyList<DivisionLeida> divisiones, int version, CancellationToken ct)
    {
        foreach (DivisionLeida d in divisiones)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO p_municipios (codigo, nombre, codigo_departamento, geometria, version_capa)
                VALUES ({d.Codigo}, {d.Nombre}, {d.CodigoPadre}, ST_SetSRID(ST_GeomFromGeoJSON({d.GeometriaGeoJson}), 4326), {version})
                ON CONFLICT (codigo) DO UPDATE SET nombre = EXCLUDED.nombre, codigo_departamento = EXCLUDED.codigo_departamento,
                    geometria = EXCLUDED.geometria, version_capa = EXCLUDED.version_capa
                WHERE p_municipios.version_capa <= EXCLUDED.version_capa", ct);
        }
    }

    /// <summary>Si un cambio llega antes que el registro (otra réplica lo procesa a la vez), se crea la fila y el registro la completa.</summary>
    private Task AsegurarFilaAsync(Guid id, DateTimeOffset instante, CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO p_desastres (id, registrado, severidad_version, estado_version, participantes, organismos_version, zona_version, actualizado_en)
        VALUES ({id}, false, 0, 0, '[]', 0, 0, {instante}) ON CONFLICT (id) DO NOTHING", ct);

    private Task EvolucionAsync(Guid id, string tipo, int version, DateTimeOffset instante, string? anterior, string? nuevo, string autor,
        CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO p_evolucion (id_desastre, tipo, version, instante, anterior, nuevo, autor)
        VALUES ({id}, {tipo}, {version}, {instante}, {anterior}, {nuevo}, {autor}) ON CONFLICT DO NOTHING", ct);
}

/// <summary>Lecturas: lo que se consulta, con la geometría simplificada ya calculada.</summary>
public sealed class LecturasPostgis : ILecturas
{
    private readonly ConsultaDbContext _db;
    public LecturasPostgis(ConsultaDbContext db) { _db = db; }

    private sealed class FilaZona
    {
        public Guid IdDesastre { get; set; }
        public int Version { get; set; }
        public DateTimeOffset VigenteDesde { get; set; }
        public string? Geometria { get; set; }
        public string[] CodigosMunicipio { get; set; } = [];
        public string Fuente { get; set; } = "";
    }

    public async Task<IReadOnlyList<FilaTablero>> TableroAsync(string? estado, string? tipo, CancellationToken ct)
    {
        IQueryable<DesastreLeidoFila> q = _db.Desastres.AsNoTracking().Where(d => d.Registrado);
        if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(d => d.Estado == estado);
        if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(d => d.Tipo == tipo);
        var filas = await q.OrderByDescending(d => d.ActualizadoEn)
            .Select(d => new { d, Municipios = _db.Zonas.Where(z => z.IdDesastre == d.Id && z.Version == d.ZonaVersion).Select(z => z.CodigosMunicipio.Length).FirstOrDefault() })
            .ToListAsync(ct);
        return filas.Select(f => Tablero(f.d, f.Municipios)).ToList();
    }

    public async Task<VistaDesastre?> DesastreAsync(Guid id, CancellationToken ct)
    {
        DesastreLeidoFila? d = await _db.Desastres.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Registrado, ct);
        if (d is null) return null;
        ZonaLeida? zona = (await ZonasAsync(id, d.ZonaVersion, ct)).FirstOrDefault();
        return new VistaDesastre(d.Id, d.Tipo, d.Nombre, d.Descripcion, d.InstanteInicio, d.Ubicacion?.Y, d.Ubicacion?.X,
            new ConMarca<string?>(d.Severidad, d.SeveridadAl), new ConMarca<string?>(d.Estado, d.EstadoAl),
            new ConMarca<string?>(d.Responsable, d.OrganismosAl), new ConMarca<IReadOnlyList<(string, string)>>(Participantes(d.Participantes), d.OrganismosAl),
            new ConMarca<ZonaLeida?>(zona, d.ZonaAl), d.ActualizadoEn);
    }

    public async Task<IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)>> MapaAsync(bool soloActivos, CancellationToken ct)
    {
        List<DesastreLeidoFila> desastres = await _db.Desastres.AsNoTracking()
            .Where(d => d.Registrado && d.ZonaVersion > 0 && (!soloActivos || d.Estado != "CERRADO")).ToListAsync(ct);
        Guid[] ids = desastres.Select(d => d.Id).ToArray();
        List<FilaZona> zonas = await _db.Database.SqlQuery<FilaZona>($@"
            SELECT z.id_desastre AS ""IdDesastre"", z.version AS ""Version"", z.vigente_desde AS ""VigenteDesde"",
                   ST_AsGeoJSON(z.geometria_simplificada, 6) AS ""Geometria"", z.codigos_municipio AS ""CodigosMunicipio"", z.fuente AS ""Fuente""
            FROM p_zonas z JOIN p_desastres d ON d.id = z.id_desastre AND d.zona_version = z.version
            WHERE z.id_desastre = ANY({ids})").ToListAsync(ct);
        return desastres.Join(zonas, d => d.Id, z => z.IdDesastre, (d, z) => (Tablero(d, z.CodigosMunicipio.Length), AZona(z))).ToList();
    }

    public async Task<(IReadOnlyList<CambioEvolucion> Cambios, IReadOnlyList<ZonaLeida> Zonas)?> EvolucionAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Desastres.AnyAsync(d => d.Id == id && d.Registrado, ct)) return null;
        List<EvolucionFila> cambios = await _db.Evolucion.AsNoTracking().Where(e => e.IdDesastre == id)
            .OrderBy(e => e.Instante).ThenBy(e => e.Version).ToListAsync(ct);
        return (cambios.Select(c => new CambioEvolucion(c.IdDesastre, c.Instante, c.Tipo, c.Anterior, c.Nuevo, c.Autor, c.Version)).ToList(),
            await ZonasAsync(id, null, ct));
    }

    public async Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) =>
        (await _db.Integraciones.AsNoTracking().OrderBy(i => i.Sistema).ToListAsync(ct))
        .Select(i => new IntegracionLeida(i.Sistema, i.Estado, i.Desde, i.MensajesEnCola)).ToList();

    private async Task<IReadOnlyList<ZonaLeida>> ZonasAsync(Guid id, int? version, CancellationToken ct)
    {
        List<FilaZona> filas = await _db.Database.SqlQuery<FilaZona>($@"
            SELECT id_desastre AS ""IdDesastre"", version AS ""Version"", vigente_desde AS ""VigenteDesde"",
                   ST_AsGeoJSON(geometria_simplificada, 6) AS ""Geometria"", codigos_municipio AS ""CodigosMunicipio"", fuente AS ""Fuente""
            FROM p_zonas WHERE id_desastre = {id} AND ({version}::int IS NULL OR version = {version}::int) ORDER BY version").ToListAsync(ct);
        return filas.Select(AZona).ToList();
    }

    private static ZonaLeida AZona(FilaZona z) => new(z.IdDesastre, z.Version, z.VigenteDesde, z.Geometria, z.CodigosMunicipio, z.Fuente);

    private static FilaTablero Tablero(DesastreLeidoFila d, int municipios) =>
        new(d.Id, d.Tipo, d.Nombre, d.Severidad, d.Estado, d.Responsable, d.ZonaVersion, municipios, d.ActualizadoEn);

    private static IReadOnlyList<(string, string)> Participantes(string json) =>
        JsonSerializer.Deserialize<List<JsonElement>>(json)!
            .Select(p => (p.GetProperty("organismo").GetString()!, p.GetProperty("responsabilidad").GetString()!)).ToList();
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoConsulta : IDesignTimeDbContextFactory<ConsultaDbContext>
{
    public ConsultaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ConsultaDbContext>().UseNpgsql("Host=localhost;Database=consulta", n => n.UseNetTopologySuite()).Options);
}
