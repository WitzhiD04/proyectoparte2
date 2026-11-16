using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EvaluacionDanos.Infraestructura.Persistencia;

/// <summary>Recuerda en el proceso que la capa de municipios ya está completa: una vez cargada no se vuelve a preguntar.</summary>
public sealed class MemoriaCapa
{
    private volatile bool _cargada;

    public bool Cargada
    {
        get => _cargada;
        set => _cargada = value;
    }
}

/// <summary>
/// Adaptador de los puertos <see cref="IAlmacenEvaluaciones"/> e <see cref="IAlmacenSincronizacion"/> sobre EF Core.
/// Las consultas son las del prototipo (ServicioSincronizacion), movidas aquí sin cambios para que el caso de uso no
/// dependa del DbContext (corrige H-12).
/// </summary>
public sealed class AlmacenEvaluacionesEf(EvaluacionDanosDbContext db, MemoriaCapa memoria) : IAlmacenSincronizacion
{
    // ---------- IAlmacenEvaluaciones ----------

    public Task<Evaluacion?> EvaluacionAsync(Guid id, CancellationToken cancelar) =>
        (from x in db.Evaluaciones where x.Id == id select x).FirstOrDefaultAsync(cancelar);

    public Task<Activo?> ActivoAsync(Guid id, CancellationToken cancelar) =>
        (from a in db.Activos where a.Id == id select a).FirstOrDefaultAsync(cancelar);

    public Task<Desastre?> DesastreAsync(Guid id, CancellationToken cancelar) =>
        (from d in db.Desastres where d.Id == id select d).FirstOrDefaultAsync(cancelar);

    public Task<Conflicto?> ConflictoAsync(Guid id, CancellationToken cancelar) =>
        (from c in db.Conflictos where c.Id == id select c).FirstOrDefaultAsync(cancelar);

    public Task<EvaluacionVersion?> VersionAsync(Guid id, CancellationToken cancelar) =>
        (from v in db.EvaluacionVersiones.AsNoTracking() where v.Id == id select v).FirstOrDefaultAsync(cancelar);

    public Task<Evidencia?> EvidenciaAsync(Guid id, CancellationToken cancelar) =>
        (from e in db.Evidencias where e.Id == id select e).FirstOrDefaultAsync(cancelar);

    public Task<TipoActivo?> TipoActivoAsync(string codigo, CancellationToken cancelar) =>
        db.TiposActivo.Include(t => t.Criterios).FirstOrDefaultAsync(t => t.Codigo == codigo, cancelar);

    public Task<List<TipoActivo>> TiposActivoAsync(CancellationToken cancelar) =>
        db.TiposActivo.Include(t => t.Criterios).OrderBy(t => t.Nombre).ToListAsync(cancelar);

    public async Task<(IReadOnlySet<string> Tipos, IReadOnlySet<Guid> Desastres)> CatalogoAsync(CancellationToken cancelar)
    {
        var tipos = await (from t in db.TiposActivo select t.Codigo).ToListAsync(cancelar);
        // Solo los desastres registrados (EV-03): uno del que apenas se conoce el id todavía no admite evaluaciones.
        var desastres = await (from d in db.Desastres where d.Registrado select d.Id).ToListAsync(cancelar);
        return (tipos.ToHashSet(), desastres.ToHashSet());
    }

    public async Task<int> UltimoNumeroVersionAsync(Guid evaluacionId, CancellationToken cancelar) =>
        await (
            from v in db.EvaluacionVersiones
            where v.EvaluacionId == evaluacionId
            select (int?)v.NumeroVersion).MaxAsync(cancelar) ?? 0;

    public Task<Guid?> EvaluacionVigenteDelActivoAsync(Guid activoId, CancellationToken cancelar) =>
        (from v in db.ActivoVersiones
         where v.ActivoId == activoId && v.EvaluacionId != null
         orderby v.Numero descending
         select v.EvaluacionId).FirstOrDefaultAsync(cancelar);

    public Task<bool> TieneConflictoPendienteComoEntranteAsync(Guid evaluacionId, Guid? excepto, CancellationToken cancelar) =>
        (from c in db.Conflictos
         where c.Estado == EstadoConflicto.PENDIENTE && c.EvaluacionEntranteId == evaluacionId
               && (excepto == null || c.Id != excepto)
         select c.Id).AnyAsync(cancelar);

    public Task<bool> TieneConflictoPendienteAsync(Guid evaluacionId, CancellationToken cancelar) =>
        (from c in db.Conflictos
         where c.Estado == EstadoConflicto.PENDIENTE
               && (c.EvaluacionEntranteId == evaluacionId || c.EvaluacionVigenteId == evaluacionId)
         select c.Id).AnyAsync(cancelar);

    public Task<bool> TieneVersionAplicadaAsync(Guid evaluacionId, CancellationToken cancelar) =>
        (from v in db.EvaluacionVersiones where v.EvaluacionId == evaluacionId && !v.EnConflicto select v.Id).AnyAsync(cancelar);

    public Task<List<Guid>> IdsEvidenciasAsync(Guid evaluacionId, CancellationToken cancelar) =>
        (from x in db.Evidencias where x.EvaluacionId == evaluacionId select x.Id).ToListAsync(cancelar);

    public void Agregar<T>(T entidad) where T : class => db.Set<T>().Add(entidad);

    public async Task GuardarAsync(CancellationToken cancelar)
    {
        try
        {
            await db.SaveChangesAsync(cancelar);
        }
        catch (DbUpdateException ex) when (EsCarrera(ex))
        {
            throw new ConcurrenciaException("Otro cambio modificó el mismo dato al mismo tiempo", ex);
        }
    }

    // ---------- IAlmacenSincronizacion ----------

    public void Limpiar() => db.ChangeTracker.Clear();

    public async Task<ITransaccionOperacion> IniciarOperacionAsync(CancellationToken cancelar) =>
        new TransaccionOperacion(db, await db.Database.BeginTransactionAsync(cancelar));

    /// <summary>
    /// Solo los choques con otro lote en paralelo se reintentan: token de concurrencia, llave duplicada
    /// (23505), serialización (40001) o interbloqueo (40P01). Un error permanente no se repite.
    /// </summary>
    public bool EsCarrera(Exception error) =>
        error is DbUpdateException ex
        && (ex is DbUpdateConcurrencyException
            || ex.InnerException is PostgresException { SqlState: "23505" or "40001" or "40P01" });

    public Task<string?> ResultadoGuardadoAsync(Guid idOperacion, CancellationToken cancelar) =>
        (from o in db.OperacionesProcesadas
         where o.IdOperacion == idOperacion
         select o.ResultadoJson).FirstOrDefaultAsync(cancelar);

    public async Task<Guid?> EvidenciaDeOtraEvaluacionAsync(IReadOnlyCollection<Guid> ids, Guid evaluacionId, CancellationToken cancelar)
    {
        var lista = ids.ToList();
        var deOtra = await (
            from x in db.Evidencias
            where lista.Contains(x.Id) && x.EvaluacionId != evaluacionId
            select x.Id).FirstOrDefaultAsync(cancelar);
        return deOtra == Guid.Empty ? null : deOtra;
    }

    /// <summary>
    /// Candado de PostgreSQL por tipo de activo, hasta el fin de la transacción. Sin él, dos lotes que crean
    /// a la vez activos cercanos del mismo tipo no ven la fila del otro (aún sin commit) y ninguno detecta el
    /// duplicado. Con él, el segundo espera al commit del primero y su búsqueda ya lo encuentra. Vale entre réplicas,
    /// porque el candado está en la base. Crear activos nuevos es raro frente a evaluar los existentes, así que
    /// serializar por tipo no estorba.
    /// </summary>
    public async Task BloquearTipoAsync(string tipoCodigo, CancellationToken cancelar)
    {
        const int espacio = 0x44414E31;   // "DAN1": separa estos candados de otros que use la base
        int clave = ClaveEstable(tipoCodigo);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({espacio}, {clave})", cancelar);
    }

    /// <summary>FNV-1a de 32 bits: igual en todos los procesos (string.GetHashCode cambia por proceso).</summary>
    private static int ClaveEstable(string texto)
    {
        uint hash = 2166136261;
        foreach (char c in texto)
        {
            hash = (hash ^ c) * 16777619;
        }
        return unchecked((int)hash);
    }

    public Task<List<CandidatoDuplicado>> CandidatosDuplicadoAsync(string tipoCodigo, Guid activoPropio,
        double minLat, double maxLat, double minLon, double maxLon, DateTime desde, DateTime hasta, CancellationToken cancelar) =>
        (from a in db.Activos.AsNoTracking()
         join ev in db.Evaluaciones.AsNoTracking() on a.Id equals ev.ActivoId
         where a.TipoActivoCodigo == tipoCodigo && a.Id != activoPropio
               && a.Lat >= minLat && a.Lat <= maxLat && a.Lon >= minLon && a.Lon <= maxLon
               && ev.InstanteCampo >= desde && ev.InstanteCampo <= hasta
         select new CandidatoDuplicado(a.Id, a.TipoActivoCodigo, a.Lat, a.Lon, ev.Id, ev.InstanteCampo))
        .ToListAsync(cancelar);

    public Task<List<VersionRegistrada>> VersionesDelActivoAsync(Guid activoId, CancellationToken cancelar) =>
        (from v in db.ActivoVersiones
         where v.ActivoId == activoId
         select new VersionRegistrada(v.Numero, v.IdDispositivo)).ToListAsync(cancelar);

    public async Task<HashSet<Guid>> EvidenciasDeclaradasAsync(Guid evaluacionId, CancellationToken cancelar) =>
        (await IdsEvidenciasAsync(evaluacionId, cancelar)).ToHashSet();

    public async Task<int?> NumeroVersionVigenteAsync(Guid evaluacionId, CancellationToken cancelar)
    {
        var versiones = await (
            from v in db.EvaluacionVersiones
            where v.EvaluacionId == evaluacionId
            orderby v.NumeroVersion descending
            select new { v.NumeroVersion, v.EnConflicto }).ToListAsync(cancelar);
        if (versiones.Count == 0)
        {
            return null;
        }
        return (versiones.FirstOrDefault(v => !v.EnConflicto) ?? versiones[0]).NumeroVersion;
    }

    private sealed class MunicipioHallado
    {
        public string Codigo { get; set; } = "";
        public int VersionCapa { get; set; }
    }

    /// <summary>
    /// Punto en polígono con ST_Contains (DA-07). Si el punto cae fuera de todos los municipios (la capa está
    /// simplificada: fronteras y costa), se toma el más cercano con el operador de distancia, que usa el índice GiST.
    /// </summary>
    public async Task<MunicipioAsignado?> MunicipioDeAsync(double lat, double lon, CancellationToken cancelar)
    {
        var dentro = await db.Database.SqlQuery<MunicipioHallado>($"""
            SELECT codigo AS "Codigo", version_capa AS "VersionCapa" FROM division_administrativa
            WHERE capa = 'MUNICIPIOS' AND char_length(codigo) = 5
              AND ST_Contains(geometria, ST_SetSRID(ST_MakePoint({lon}, {lat}), 4326))
            ORDER BY codigo LIMIT 1
            """).ToListAsync(cancelar);
        if (dentro.Count == 0)
        {
            dentro = await db.Database.SqlQuery<MunicipioHallado>($"""
                SELECT codigo AS "Codigo", version_capa AS "VersionCapa" FROM division_administrativa
                WHERE capa = 'MUNICIPIOS' AND char_length(codigo) = 5
                ORDER BY geometria <-> ST_SetSRID(ST_MakePoint({lon}, {lat}), 4326), codigo LIMIT 1
                """).ToListAsync(cancelar);
        }
        return dentro.Count == 0 ? null : new MunicipioAsignado(dentro[0].Codigo, dentro[0].VersionCapa);
    }

    public async Task<bool> CapaDeMunicipiosCargadaAsync(CancellationToken cancelar)
    {
        if (memoria.Cargada)
        {
            return true;
        }
        // Completa: llegaron todas las partes de alguna versión (EV-21 se publica por partes, E-11).
        var completas = await db.Database.SqlQuery<int>($"""
            SELECT version_capa AS "Value" FROM capa_parte WHERE capa = 'MUNICIPIOS'
            GROUP BY version_capa, total HAVING count(*) = total LIMIT 1
            """).ToListAsync(cancelar);
        memoria.Cargada = completas.Count > 0;
        return memoria.Cargada;
    }

    private sealed class TransaccionOperacion(EvaluacionDanosDbContext db, IDbContextTransaction transaccion) : ITransaccionOperacion
    {
        public async Task GuardarYConfirmarAsync(CancellationToken cancelar)
        {
            await db.SaveChangesAsync(cancelar);
            await transaccion.CommitAsync(cancelar);
        }

        public ValueTask DisposeAsync() => transaccion.DisposeAsync();
    }
}

/// <summary>Adaptador del puerto <see cref="ICopiasLocales"/>: desastres (E-29) y capa geográfica en PostGIS (DA-07).</summary>
public sealed class CopiasLocalesEf(EvaluacionDanosDbContext db) : ICopiasLocales
{
    public async Task<Desastre> DesastreAsync(Guid id, CancellationToken cancelar)
    {
        var desastre = await db.Desastres.FirstOrDefaultAsync(d => d.Id == id, cancelar);
        if (desastre is null)
        {
            desastre = Desastre.Desconocido(id);
            db.Desastres.Add(desastre);
        }
        return desastre;
    }

    /// <summary>
    /// Una sentencia por parte. ST_MakeValid repara los polígonos que la simplificación de la capa dejó con cruces
    /// (un polígono inválido puede hacer fallar ST_Contains). Corre dentro de la transacción del consumidor.
    /// </summary>
    public async Task GuardarParteDeCapaAsync(string capa, int versionCapa, int parte, int total,
        IReadOnlyList<DivisionRecibida> divisiones, CancellationToken cancelar)
    {
        string[] codigos = divisiones.Select(d => d.Codigo).ToArray();
        string[] nombres = divisiones.Select(d => d.Nombre.Length <= Limites.Nombre ? d.Nombre : d.Nombre[..Limites.Nombre]).ToArray();
        string?[] padres = divisiones.Select(d => d.CodigoPadre).ToArray();
        string[] geometrias = divisiones.Select(d => d.GeometriaGeoJson).ToArray();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO division_administrativa (capa, codigo, nombre, codigo_padre, version_capa, geometria)
            SELECT {capa}, d.codigo, d.nombre, d.padre, {versionCapa},
                   ST_Multi(ST_CollectionExtract(ST_MakeValid(ST_SetSRID(ST_GeomFromGeoJSON(d.geometria), 4326)), 3))
            FROM unnest({codigos}, {nombres}, {padres}, {geometrias}) AS d(codigo, nombre, padre, geometria)
            ON CONFLICT (capa, codigo) DO UPDATE SET nombre = EXCLUDED.nombre, codigo_padre = EXCLUDED.codigo_padre,
                version_capa = EXCLUDED.version_capa, geometria = EXCLUDED.geometria
            """, cancelar);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO capa_parte (capa, version_capa, parte, total, recibida_en)
            VALUES ({capa}, {versionCapa}, {parte}, {total}, now()) ON CONFLICT DO NOTHING
            """, cancelar);
    }
}
