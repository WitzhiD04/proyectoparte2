using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using Microsoft.EntityFrameworkCore;

// EF1002: las consultas "Raw" solo interpolan constantes de este archivo (columnas y filtros); los valores van como parámetros.
#pragma warning disable EF1002

namespace ConsultaDanos.Infraestructura;

public sealed class RegistroZona
{
    public string Codigo { get; set; } = "";
    public string? Nombre { get; set; }
    public string? CodigoDepartamento { get; set; }
    public int VersionCapa { get; set; }
    public DateTimeOffset? CapaAl { get; set; }
    public string Nivel { get; set; } = "";
    public int Cantidad { get; set; }
    public int ConConflicto { get; set; }
}

public sealed class RegistroPunto
{
    public Guid IdActivo { get; set; }
    public Guid IdEvaluacion { get; set; }
    public string TipoActivo { get; set; } = "";
    public string Nivel { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string? EstadoValidacion { get; set; }
    public bool Conflicto { get; set; }
    public string CodigoMunicipio { get; set; } = "";
    public DateTimeOffset ActualizadoAl { get; set; }
}

public sealed class RegistroGeometria
{
    public string Codigo { get; set; } = "";
    public string Geometria { get; set; } = "";
}

public sealed class RegistroConsolidado
{
    public string Codigo { get; set; } = "";
    public string? Nombre { get; set; }
    public string? CodigoDepartamento { get; set; }
    public string TipoActivo { get; set; } = "";
    public string? NombreTipo { get; set; }
    public string Nivel { get; set; } = "";
    public int Cantidad { get; set; }
}

public sealed class RegistroHistorial
{
    public Guid IdActivo { get; set; }
    public Guid IdEvaluacion { get; set; }
    public int VersionEvaluacion { get; set; }
    public string Origen { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    public string Nivel { get; set; } = "";
    public string TipoActivo { get; set; } = "";
    public string EstadoEvaluacion { get; set; } = "";
    public string? EstadoValidacion { get; set; }
    public bool EnConflicto { get; set; }
    public Guid? IdDesastre { get; set; }
    public string CodigoMunicipio { get; set; } = "";
    public string? Autor { get; set; }
    public string? Detalle { get; set; }

    public EntradaHistorial AEntrada() => new(IdActivo, IdEvaluacion, VersionEvaluacion, Origen, Instante, Enum.Parse<NivelDano>(Nivel),
        TipoActivo, EstadoEvaluacion, EstadoValidacion is null ? null : Enum.Parse<EstadoValidacion>(EstadoValidacion), EnConflicto,
        IdDesastre, CodigoMunicipio, Autor, Detalle);
}

public sealed class RegistroBandeja
{
    public int Pendientes { get; set; }
    public DateTimeOffset? MasViejo { get; set; }
    public DateTimeOffset? DatoAl { get; set; }
}

/// <summary>
/// Lecturas de las proyecciones. El mapa agrega por el índice del desastre (los filtros de nivel y de validación se
/// aplican al consultar); el consolidado ya está agregado en p_dano_por_zona. La geometría ya viene simplificada.
/// </summary>
public sealed class LecturasPostgis : ILecturasDanos
{
    // Que el activo tenga un conflicto pendiente se calcula al consultar: así EV-10 y EV-22 pueden llegar en cualquier orden.
    private const string Conflicto = "EXISTS (SELECT 1 FROM p_conflictos c WHERE c.id_activo = v.id_activo AND c.estado = 'PENDIENTE')";

    private const string ColumnasHistorial = @"
        h.id_activo AS ""IdActivo"", h.id_evaluacion AS ""IdEvaluacion"", h.version_evaluacion AS ""VersionEvaluacion"", h.origen AS ""Origen"",
        h.instante AS ""Instante"", h.nivel_dano AS ""Nivel"", h.tipo_activo AS ""TipoActivo"", h.estado_evaluacion AS ""EstadoEvaluacion"",
        h.estado_validacion AS ""EstadoValidacion"", h.en_conflicto AS ""EnConflicto"", h.id_desastre AS ""IdDesastre"",
        h.codigo_municipio AS ""CodigoMunicipio"", h.autor AS ""Autor"", h.detalle AS ""Detalle""";

    private readonly ConsultaDanosDbContext _db;
    public LecturasPostgis(ConsultaDanosDbContext db) { _db = db; }

    /// <summary>
    /// Filtro común de mapa y puntos: activos con contenido, del desastre, no fusionados, con nivel al menos el mínimo;
    /// solo validados, o todos menos los rechazados (una evaluación rechazada no describe el daño del activo).
    /// </summary>
    private static string Filtro(string alias) => $@"
        {alias}.id_desastre = {{0}} AND {alias}.version_activo >= 0 AND NOT {alias}.fusionado AND {alias}.nivel_dano = ANY({{1}})
        AND ({alias}.validada OR (NOT {{2}} AND {alias}.estado_validacion IS DISTINCT FROM 'RECHAZADA'))";

    private static object[] Parametros(FiltroMapa f) => [f.Desastre, f.Niveles.Select(n => n.ToString()).ToArray(), f.SoloValidadas];

    public async Task<IReadOnlyList<ZonaMapa>> ZonasMapaAsync(FiltroMapa filtro, CancellationToken ct)
    {
        List<RegistroZona> filas = await _db.Database.SqlQueryRaw<RegistroZona>($@"
            SELECT v.codigo_municipio AS ""Codigo"", m.nombre AS ""Nombre"",
                   COALESCE(m.codigo_departamento, left(v.codigo_municipio, 2)) AS ""CodigoDepartamento"",
                   COALESCE(m.version_capa, 0) AS ""VersionCapa"", m.actualizado_al AS ""CapaAl"", v.nivel_dano AS ""Nivel"",
                   count(*)::int AS ""Cantidad"", (count(*) FILTER (WHERE {Conflicto}))::int AS ""ConConflicto""
            FROM p_evaluacion_vigente v LEFT JOIN p_municipios m ON m.codigo = v.codigo_municipio
            WHERE {Filtro("v")}
            GROUP BY v.codigo_municipio, m.nombre, m.codigo_departamento, m.version_capa, m.actualizado_al, v.nivel_dano",
            Parametros(filtro)).ToListAsync(ct);

        return filas.GroupBy(f => f.Codigo, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            RegistroZona p = g.First();
            Distribucion d = g.Aggregate(new Distribucion(), (acc, f) => acc.Sumar(Enum.Parse<NivelDano>(f.Nivel), f.Cantidad));
            return new ZonaMapa(g.Key, p.Nombre, p.CodigoDepartamento, p.VersionCapa, p.CapaAl, d, g.Sum(f => f.ConConflicto));
        }).ToList();
    }

    public async Task<IReadOnlyList<PuntoMapa>> PuntosAsync(FiltroMapa filtro, CancellationToken ct)
    {
        List<RegistroPunto> filas = await _db.Database.SqlQueryRaw<RegistroPunto>($@"
            SELECT v.id_activo AS ""IdActivo"", v.id_evaluacion AS ""IdEvaluacion"", v.tipo_activo AS ""TipoActivo"", v.nivel_dano AS ""Nivel"",
                   ST_Y(v.ubicacion) AS ""Lat"", ST_X(v.ubicacion) AS ""Lon"", v.estado_validacion AS ""EstadoValidacion"",
                   {Conflicto} AS ""Conflicto"", v.codigo_municipio AS ""CodigoMunicipio"", v.actualizado_al AS ""ActualizadoAl""
            FROM p_evaluacion_vigente v
            WHERE {Filtro("v")}
            ORDER BY v.id_activo", Parametros(filtro)).ToListAsync(ct);
        return filas.Select(f => new PuntoMapa(f.IdActivo, f.IdEvaluacion, f.TipoActivo, Enum.Parse<NivelDano>(f.Nivel), f.Lat, f.Lon,
            f.EstadoValidacion is null ? null : Enum.Parse<EstadoValidacion>(f.EstadoValidacion), f.Conflicto, f.CodigoMunicipio,
            f.ActualizadoAl)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> GeometriasAsync(IReadOnlyCollection<string> codigos, CancellationToken ct)
    {
        string[] lista = codigos.ToArray();
        List<RegistroGeometria> filas = await _db.Database.SqlQuery<RegistroGeometria>($@"
            SELECT codigo AS ""Codigo"", ST_AsGeoJSON(geometria, 6) AS ""Geometria"" FROM p_municipios WHERE codigo = ANY({lista})")
            .ToListAsync(ct);
        return filas.ToDictionary(f => f.Codigo, f => f.Geometria, StringComparer.Ordinal);
    }

    public async Task<DateTimeOffset?> DatoAlAsync(Guid desastre, CancellationToken ct)
    {
        List<DateTimeOffset> filas = await _db.Database.SqlQuery<DateTimeOffset>($@"
            SELECT dato_al AS ""Value"" FROM p_desastres WHERE id_desastre = {desastre}").ToListAsync(ct);
        return filas.Count == 0 ? null : filas[0];
    }

    public async Task<IReadOnlyList<ZonaConsolidada>> ConsolidadoAsync(Guid desastre, CancellationToken ct)
    {
        List<RegistroConsolidado> filas = await _db.Database.SqlQuery<RegistroConsolidado>($@"
            SELECT z.codigo_municipio AS ""Codigo"", m.nombre AS ""Nombre"",
                   COALESCE(m.codigo_departamento, left(z.codigo_municipio, 2)) AS ""CodigoDepartamento"",
                   z.tipo_activo AS ""TipoActivo"", t.nombre AS ""NombreTipo"", z.nivel_dano AS ""Nivel"", z.cantidad AS ""Cantidad""
            FROM p_dano_por_zona z
                LEFT JOIN p_municipios m ON m.codigo = z.codigo_municipio
                LEFT JOIN p_tipos_activo t ON t.codigo = z.tipo_activo
            WHERE z.id_desastre = {desastre} AND z.cantidad > 0").ToListAsync(ct);
        return filas.GroupBy(f => f.Codigo, StringComparer.Ordinal).Select(g => new ZonaConsolidada(g.Key, g.First().Nombre,
            g.First().CodigoDepartamento,
            g.Select(f => new ConteoTipo(f.TipoActivo, f.NombreTipo, Enum.Parse<NivelDano>(f.Nivel), f.Cantidad)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) =>
        (await _db.Integraciones.AsNoTracking().OrderBy(i => i.Sistema).ToListAsync(ct))
        .Select(i => new IntegracionLeida(i.Sistema, i.Estado, i.Desde, i.MensajesEnCola)).ToList();

    public async Task<EvolucionActivo?> EvolucionAsync(Guid idActivo, CancellationToken ct)
    {
        List<RegistroActivo> fila = await _db.Database.SqlQueryRaw<RegistroActivo>(
            $"SELECT {RegistroActivo.Columnas}, {Conflicto} AS \"Conflicto\" FROM p_evaluacion_vigente v WHERE v.id_activo = {{0}}", idActivo)
            .ToListAsync(ct);
        if (fila.Count == 0)
        {
            return null;
        }
        List<RegistroHistorial> historial = await _db.Database.SqlQueryRaw<RegistroHistorial>($@"
            SELECT {ColumnasHistorial}
            FROM p_historial_activo h WHERE h.id_activo = {{0}}
            ORDER BY h.instante, h.version_evaluacion, h.origen", idActivo).ToListAsync(ct);
        List<EntradaHistorial> entradas = historial.Select(h => h.AEntrada()).ToList();
        RegistroActivo a = fila[0];
        DateTimeOffset datoAl = entradas.Count == 0 || a.ActualizadoAl > entradas.Max(e => e.Instante)
            ? a.ActualizadoAl : entradas.Max(e => e.Instante);
        return new EvolucionActivo(idActivo, a.AEstado(), a.Conflicto, entradas, datoAl);
    }

    public async Task<string?> NombreMunicipioAsync(string codigo, CancellationToken ct) =>
        (await _db.Database.SqlQuery<string>($@"SELECT nombre AS ""Value"" FROM p_municipios WHERE codigo = {codigo}")
            .ToListAsync(ct)).FirstOrDefault();

    public async Task<bool> DepartamentoEnCapaAsync(string codigo, CancellationToken ct) =>
        (await _db.Database.SqlQuery<bool>($@"
            SELECT EXISTS (SELECT 1 FROM p_municipios m WHERE COALESCE(m.codigo_departamento, left(m.codigo, 2)) = {codigo}) AS ""Value""")
            .ToListAsync(ct)).Single();

    /// <summary>
    /// El departamento de cada entrada se resuelve como en el mapa: el de la capa y, si el municipio aún no está en ella,
    /// los dos primeros dígitos de su código.
    /// </summary>
    public async Task<IReadOnlyList<EntradaHistorial>> HistorialZonaAsync(string codigo, TipoZona tipo, Guid? desastre, CancellationToken ct)
    {
        string zona = tipo == TipoZona.MUNICIPIO
            ? "h.codigo_municipio = {0}"
            : "COALESCE(m.codigo_departamento, left(h.codigo_municipio, 2)) = {0}";
        string delDesastre = desastre is null ? "" : "AND h.id_desastre = {1}";
        object[] parametros = desastre is null ? [codigo] : [codigo, desastre.Value];
        List<RegistroHistorial> filas = await _db.Database.SqlQueryRaw<RegistroHistorial>($@"
            SELECT {ColumnasHistorial}
            FROM p_historial_activo h LEFT JOIN p_municipios m ON m.codigo = h.codigo_municipio
            WHERE {zona} {delDesastre}
            ORDER BY h.instante, h.id_activo, h.version_evaluacion, h.origen", parametros).ToListAsync(ct);
        return filas.Select(h => h.AEntrada()).ToList();
    }

    public async Task<BandejaLeida> BandejaAsync(CancellationToken ct)
    {
        RegistroBandeja b = (await _db.Database.SqlQuery<RegistroBandeja>($@"
            SELECT pendientes AS ""Pendientes"", mas_viejo AS ""MasViejo"", dato_al AS ""DatoAl"" FROM p_bandeja WHERE id = 1")
            .ToListAsync(ct)).Single();
        return new BandejaLeida(b.Pendientes, b.MasViejo, b.DatoAl);
    }
}
