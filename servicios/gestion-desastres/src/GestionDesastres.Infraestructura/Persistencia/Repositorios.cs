using System.Text.Json;
using GestionDesastres.Aplicacion;
using GestionDesastres.Dominio;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using NetTopologySuite.Operation.Union;
using Npgsql;

namespace GestionDesastres.Infraestructura.Persistencia;

// Todos los adaptadores de escritura comparten el DbContext de la petición. Ninguno guarda por su cuenta: la unidad de
// trabajo confirma todo junto, y por eso el cambio, su historial, su zona y sus eventos quedan en una sola transacción.

/// <summary>Conversión entre GeoJSON (texto, como lo ve el dominio) y geometrías de PostGIS (SRID 4326).</summary>
public static class Geo
{
    public const int Srid = 4326;
    private static readonly JsonSerializerOptions Opciones = Crear();
    private static readonly GeometryFactory Fabrica = new(new PrecisionModel(), Srid);

    private static JsonSerializerOptions Crear()
    {
        JsonSerializerOptions o = new();
        o.Converters.Add(new GeoJsonConverterFactory(Fabrica));
        return o;
    }

    public static Geometry Leer(string geoJson)
    {
        Geometry geometria;
        try
        {
            geometria = JsonSerializer.Deserialize<Geometry>(geoJson, Opciones)
                ?? throw new ReglaDeNegocioException("GEOMETRIA_INVALIDA", "La geometría está vacía.");
        }
        catch (JsonException e)
        {
            throw new ReglaDeNegocioException("GEOMETRIA_INVALIDA", "La geometría no es GeoJSON válido: " + e.Message);
        }
        geometria.SRID = Srid;
        return geometria;
    }

    public static string Escribir(Geometry geometria) => JsonSerializer.Serialize(geometria, Opciones);

    public static Point Punto(double lat, double lon) => Fabrica.CreatePoint(new Coordinate(lon, lat));
}

internal static class JsonInfra
{
    public static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);
}

public sealed class RepositorioDesastresEf : IRepositorioDesastres
{
    private readonly GestionDesastresDbContext _db;
    public RepositorioDesastresEf(GestionDesastresDbContext db) { _db = db; }

    public async Task<Desastre?> BuscarAsync(Guid id, CancellationToken ct = default)
    {
        DesastreFila? fila = await _db.Desastres.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (fila is null)
        {
            return null;
        }
        // El estado vigente es el de la última entrada del historial (AD-20).
        HistorialFila ultima = await _db.Historial.AsNoTracking().Where(h => h.IdDesastre == id)
            .OrderByDescending(h => h.Version).FirstAsync(ct);
        EstadoResultante estado = JsonSerializer.Deserialize<EstadoResultante>(ultima.Resultado, JsonInfra.Opciones)!;
        List<ZonaFila> zonas = await _db.Zonas.AsNoTracking().Where(z => z.IdDesastre == id).OrderBy(z => z.Version).ToListAsync(ct);

        return Desastre.Rehidratar(new InstantaneaDesastre(
            fila.Id, fila.CodigoTipo, fila.Nombre, fila.Descripcion, fila.InstanteInicio, new Ubicacion(fila.Ubicacion.Y, fila.Ubicacion.X),
            fila.OrganismosRegistra, fila.Fuentes, estado.Responsable, estado.Participantes, estado.Severidad, estado.Estado,
            estado.CodigoPerfil, zonas.Select(AZona).ToList(), fila.Version, fila.InstanteActualizacion));
    }

    public async Task GuardarAsync(Desastre desastre, CancellationToken ct = default)
    {
        DesastreFila? fila = _db.Desastres.Local.FirstOrDefault(d => d.Id == desastre.Id);
        if (fila is null)
        {
            fila = new DesastreFila
            {
                Id = desastre.Id,
                CodigoTipo = desastre.CodigoTipo,
                Nombre = desastre.Nombre,
                Descripcion = desastre.Descripcion,
                InstanteInicio = desastre.InstanteInicio,
                Ubicacion = Geo.Punto(desastre.Ubicacion.Lat, desastre.Ubicacion.Lon),
                OrganismosRegistra = desastre.OrganismosRegistra.ToArray(),
                Fuentes = desastre.Fuentes.ToArray(),
            };
            _db.Desastres.Add(fila);
        }
        fila.Version = desastre.Version;
        fila.InstanteActualizacion = desastre.InstanteActualizacion;

        foreach (CambioDesastre c in desastre.CambiosNuevos)
        {
            _db.Historial.Add(new HistorialFila
            {
                IdDesastre = c.IdDesastre, Version = c.Version, TipoCambio = c.Tipo.ToString(), ValorAnterior = c.ValorAnterior,
                ValorNuevo = c.ValorNuevo, Autor = c.Autor, Organizacion = c.Organizacion, Justificacion = c.Justificacion,
                Instante = c.Instante, Resultado = JsonSerializer.Serialize(c.Resultado, JsonInfra.Opciones),
            });
        }
        // Las zonas nuevas son las de versión mayor que la guardada.
        int ultimaGuardada = await _db.Zonas.Where(z => z.IdDesastre == desastre.Id).Select(z => (int?)z.Version).MaxAsync(ct) ?? 0;
        foreach (VersionZona z in desastre.Zonas.Where(z => z.Version > ultimaGuardada))
        {
            _db.Zonas.Add(new ZonaFila
            {
                IdDesastre = desastre.Id, Version = z.Version, VigenteDesde = z.VigenteDesde,
                Geometria = z.GeometriaGeoJson is null ? null : Geo.Leer(z.GeometriaGeoJson), CodigosMunicipio = z.CodigosMunicipio.ToArray(),
                Fuente = z.Fuente.ToString(), Autor = z.Autor, Justificacion = z.Justificacion,
            });
        }
    }

    public async Task<IReadOnlyList<CambioDesastre>> HistorialAsync(Guid id, CancellationToken ct = default)
    {
        List<HistorialFila> filas = await _db.Historial.AsNoTracking().Where(h => h.IdDesastre == id).OrderBy(h => h.Version).ToListAsync(ct);
        return filas.Select(f => new CambioDesastre(f.IdDesastre, f.Version, Enum.Parse<TipoCambio>(f.TipoCambio), f.ValorAnterior, f.ValorNuevo,
            f.Autor, f.Organizacion, f.Justificacion, f.Instante, JsonSerializer.Deserialize<EstadoResultante>(f.Resultado, JsonInfra.Opciones)!)).ToList();
    }

    private static VersionZona AZona(ZonaFila z) => new(z.Version, z.VigenteDesde, z.Geometria is null ? null : Geo.Escribir(z.Geometria),
        z.CodigosMunicipio, Enum.Parse<FuenteDelimitacion>(z.Fuente), z.Autor, z.Justificacion);
}

public sealed class CatalogoEf : ICatalogo
{
    private readonly GestionDesastresDbContext _db;
    public CatalogoEf(GestionDesastresDbContext db) { _db = db; }

    public async Task<TipoDesastre?> TipoAsync(string codigo, CancellationToken ct = default)
    {
        string clave = codigo.Trim().ToUpperInvariant();
        TipoDesastreFila? f = await _db.Tipos.AsNoTracking().FirstOrDefaultAsync(t => t.Codigo == clave, ct);
        return f is null ? null : ATipo(f);
    }

    public async Task<IReadOnlyList<TipoDesastre>> TiposAsync(CancellationToken ct = default) =>
        (await _db.Tipos.AsNoTracking().OrderBy(t => t.Nombre).ToListAsync(ct)).Select(ATipo).ToList();

    public async Task GuardarTipoAsync(TipoDesastre tipo, bool nuevo, CancellationToken ct = default)
    {
        TipoDesastreFila fila = nuevo ? new TipoDesastreFila { Codigo = tipo.Codigo } : await _db.Tipos.FirstAsync(t => t.Codigo == tipo.Codigo, ct);
        fila.Nombre = tipo.Nombre;
        fila.CodigoPerfil = tipo.CodigoPerfil;
        fila.Criterios = JsonSerializer.Serialize(tipo.CriteriosPorNivel.ToDictionary(p => p.Key.ToString(), p => p.Value), JsonInfra.Opciones);
        fila.Organismos = JsonSerializer.Serialize(tipo.OrganismosPorNivel.ToDictionary(p => p.Key.ToString(), p => p.Value), JsonInfra.Opciones);
        fila.VigenteDesde = tipo.VigenteDesde;
        fila.VigenteHasta = tipo.VigenteHasta;
        if (nuevo)
        {
            _db.Tipos.Add(fila);
        }
    }

    public async Task<PerfilCicloVida?> PerfilAsync(string codigo, CancellationToken ct = default)
    {
        string clave = codigo.Trim().ToUpperInvariant();
        PerfilFila? f = await _db.Perfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == clave, ct);
        return f is null ? null : APerfil(f);
    }

    public async Task<IReadOnlyList<PerfilCicloVida>> PerfilesAsync(CancellationToken ct = default) =>
        (await _db.Perfiles.AsNoTracking().OrderBy(p => p.Rango).ToListAsync(ct)).Select(APerfil).ToList();

    public async Task GuardarPerfilAsync(PerfilCicloVida perfil, bool nuevo, CancellationToken ct = default)
    {
        PerfilFila fila = nuevo ? new PerfilFila { Codigo = perfil.Codigo } : await _db.Perfiles.FirstAsync(p => p.Codigo == perfil.Codigo, ct);
        fila.Nombre = perfil.Nombre;
        fila.Rango = perfil.Rango;
        fila.Estados = perfil.Estados.Select(e => e.ToString()).Order().ToArray();
        if (nuevo)
        {
            _db.Perfiles.Add(fila);
        }
    }

    public async Task<PerfilPorNivel> PerfilPorNivelAsync(CancellationToken ct = default)
    {
        List<PerfilPorNivelFila> filas = await _db.PerfilPorNivel.AsNoTracking().ToListAsync(ct);
        return filas.Count == 0
            ? PerfilPorNivel.Inicial()
            : new PerfilPorNivel(filas.ToDictionary(f => Enum.Parse<Severidad>(f.Nivel), f => f.CodigoPerfil));
    }

    public async Task GuardarPerfilPorNivelAsync(PerfilPorNivel valor, CancellationToken ct = default)
    {
        List<PerfilPorNivelFila> filas = await _db.PerfilPorNivel.ToListAsync(ct);
        foreach ((Severidad nivel, string perfil) in valor.Minimos)
        {
            PerfilPorNivelFila? fila = filas.FirstOrDefault(f => f.Nivel == nivel.ToString());
            if (fila is null)
            {
                _db.PerfilPorNivel.Add(new PerfilPorNivelFila { Nivel = nivel.ToString(), CodigoPerfil = perfil });
            }
            else
            {
                fila.CodigoPerfil = perfil;
            }
        }
    }

    private static TipoDesastre ATipo(TipoDesastreFila f)
    {
        Dictionary<string, string> criterios = JsonSerializer.Deserialize<Dictionary<string, string>>(f.Criterios, JsonInfra.Opciones) ?? new();
        Dictionary<string, List<Participante>> organismos =
            JsonSerializer.Deserialize<Dictionary<string, List<Participante>>>(f.Organismos, JsonInfra.Opciones) ?? new();
        return new TipoDesastre(f.Codigo, f.Nombre, f.CodigoPerfil,
            criterios.ToDictionary(p => Enum.Parse<Severidad>(p.Key), p => p.Value),
            organismos.ToDictionary(p => Enum.Parse<Severidad>(p.Key), p => (IReadOnlyList<Participante>)p.Value),
            f.VigenteDesde, f.VigenteHasta);
    }

    private static PerfilCicloVida APerfil(PerfilFila f) => new(f.Codigo, f.Nombre, f.Rango, f.Estados.Select(Enum.Parse<EstadoDesastre>));
}

public sealed class ProyectosDelDesastreEf : IProyectosDelDesastre
{
    private readonly GestionDesastresDbContext _db;
    public ProyectosDelDesastreEf(GestionDesastresDbContext db) { _db = db; }

    public Task<int> NoFinalesAsync(Guid idDesastre, CancellationToken ct = default) =>
        _db.Proyectos.CountAsync(p => p.IdDesastre == idDesastre && !p.Final, ct);

    public Task AplicarAsync(Guid idProyecto, Guid idDesastre, bool final, int version, CancellationToken ct = default) =>
        // Solo una versión más nueva cambia la fila: un evento repetido o atrasado no tiene efecto.
        _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO proyectos_desastre (id_proyecto, id_desastre, final, version) VALUES ({idProyecto}, {idDesastre}, {final}, {version})
            ON CONFLICT (id_proyecto) DO UPDATE SET final = EXCLUDED.final, version = EXCLUDED.version
            WHERE proyectos_desastre.version < EXCLUDED.version", ct);
}

/// <summary>La capa de municipios en PostGIS (DA-07): completa delimitaciones y guarda lo que publica SE-2.</summary>
public sealed class CapaMunicipiosPostgis : ICapaMunicipios
{
    private readonly GestionDesastresDbContext _db;
    public CapaMunicipiosPostgis(GestionDesastresDbContext db) { _db = db; }

    public async Task<(string? Geometria, IReadOnlyList<string> Municipios)> CompletarAsync(string? geometria, IReadOnlyList<string> municipios,
        CancellationToken ct = default)
    {
        bool capaCargada = await _db.Municipios.AnyAsync(ct);
        HashSet<string> codigos = municipios.Select(m => m.Trim()).Where(m => m.Length > 0).ToHashSet();
        if (capaCargada && codigos.Count > 0)
        {
            List<string> conocidos = await _db.Municipios.Where(m => codigos.Contains(m.Codigo)).Select(m => m.Codigo).ToListAsync(ct);
            List<string> desconocidos = codigos.Except(conocidos).Order().ToList();
            if (desconocidos.Count > 0)
            {
                throw new ReglaDeNegocioException("MUNICIPIO_DESCONOCIDO", $"Municipios que no están en la capa de SE-2: {string.Join(", ", desconocidos)}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(geometria))
        {
            Geometry poligono = Geo.Leer(geometria);
            if (poligono is not (Polygon or MultiPolygon) || !poligono.IsValid)
            {
                throw new ReglaDeNegocioException("GEOMETRIA_INVALIDA", "La zona debe ser un Polygon o un MultiPolygon válido (RF-GD-08).");
            }
            if (capaCargada)
            {
                // Los municipios que interseca el polígono se agregan a la zona (DA-07): así el mapa y la jurisdicción los conocen.
                List<string> intersecados = await _db.Municipios.Where(m => m.Geometria.Intersects(poligono)).Select(m => m.Codigo).ToListAsync(ct);
                codigos.UnionWith(intersecados);
            }
            return (Geo.Escribir(poligono), codigos.Order().ToList());
        }

        // Solo municipios: la geometría de la zona es su unión, para que el mapa la dibuje sin calcularla (AD-17).
        List<Geometry> geometrias = await _db.Municipios.Where(m => codigos.Contains(m.Codigo)).Select(m => m.Geometria).ToListAsync(ct);
        string? union = geometrias.Count == 0 ? null : Geo.Escribir(UnaryUnionOp.Union(geometrias));
        return (union, codigos.Order().ToList());
    }

    public async Task AplicarAsync(IReadOnlyList<DivisionGeografica> divisiones, int versionCapa, CancellationToken ct = default)
    {
        foreach (DivisionGeografica d in divisiones)
        {
            Geometry geometria = Geo.Leer(d.GeometriaGeoJson);
            MunicipioFila? fila = await _db.Municipios.FirstOrDefaultAsync(m => m.Codigo == d.Codigo, ct);
            if (fila is null)
            {
                _db.Municipios.Add(new MunicipioFila
                {
                    Codigo = d.Codigo, Nombre = d.Nombre, CodigoDepartamento = d.CodigoPadre, Geometria = geometria, VersionCapa = versionCapa,
                });
            }
            else if (fila.VersionCapa <= versionCapa)
            {
                fila.Nombre = d.Nombre;
                fila.CodigoDepartamento = d.CodigoPadre;
                fila.Geometria = geometria;
                fila.VersionCapa = versionCapa;
            }
        }
    }
}

public sealed class UnidadDeTrabajoEf : IUnidadDeTrabajo
{
    private readonly GestionDesastresDbContext _db;
    public UnidadDeTrabajoEf(GestionDesastresDbContext db) { _db = db; }

    public async Task ConfirmarAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictoDeConcurrenciaException("Otro usuario modificó el desastre al mismo tiempo. Vuelva a cargarlo e intente de nuevo.");
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictoDeConcurrenciaException("La operación chocó con otra igual que se registró primero.");
        }
    }
}

public sealed class RelojDelSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}
