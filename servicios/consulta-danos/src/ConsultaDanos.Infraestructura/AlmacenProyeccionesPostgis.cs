using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using Microsoft.EntityFrameworkCore;

// EF1002: las consultas "Raw" solo interpolan constantes de este archivo (columnas y filtros); los valores van como parámetros.
#pragma warning disable EF1002

namespace ConsultaDanos.Infraestructura;

/// <summary>Una fila de p_evaluacion_vigente leída con SQL (la ubicación como latitud y longitud).</summary>
public sealed class RegistroActivo
{
    public Guid IdActivo { get; set; }
    public Guid? IdEvaluacion { get; set; }
    public Guid? IdDesastre { get; set; }
    public string? TipoActivo { get; set; }
    public string? NivelDano { get; set; }
    public string? CodigoMunicipio { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double? PrecisionM { get; set; }
    public string? EstadoEvaluacion { get; set; }
    public int VersionEvaluacion { get; set; }
    public int VersionActivo { get; set; }
    public short OrigenContenido { get; set; }
    public string? EstadoValidacion { get; set; }
    public bool Fusionado { get; set; }
    public Guid? FusionadoEn { get; set; }
    public DateTimeOffset ActualizadoAl { get; set; }
    /// <summary>Si tiene un conflicto pendiente; solo lo calcula la consulta de evolución.</summary>
    public bool Conflicto { get; set; }

    public const string Columnas = @"id_activo AS ""IdActivo"", id_evaluacion AS ""IdEvaluacion"", id_desastre AS ""IdDesastre"",
        tipo_activo AS ""TipoActivo"", nivel_dano AS ""NivelDano"", codigo_municipio AS ""CodigoMunicipio"",
        ST_Y(ubicacion) AS ""Lat"", ST_X(ubicacion) AS ""Lon"", precision_m AS ""PrecisionM"", estado_evaluacion AS ""EstadoEvaluacion"",
        version_evaluacion AS ""VersionEvaluacion"", version_activo AS ""VersionActivo"", origen_contenido AS ""OrigenContenido"",
        estado_validacion AS ""EstadoValidacion"", fusionado AS ""Fusionado"", fusionado_en AS ""FusionadoEn"", actualizado_al AS ""ActualizadoAl""";

    public EstadoActivo AEstado()
    {
        ContenidoActivo? contenido = VersionActivo >= 0 && IdEvaluacion is Guid e && NivelDano is not null
            ? new ContenidoActivo(e, IdDesastre, TipoActivo!, Enum.Parse<NivelDano>(NivelDano), CodigoMunicipio!, Lat!.Value, Lon!.Value,
                PrecisionM, EstadoEvaluacion!, VersionEvaluacion)
            : null;
        return new EstadoActivo(IdActivo, contenido, VersionActivo, (OrigenContenido)OrigenContenido,
            EstadoValidacion is null ? null : Enum.Parse<EstadoValidacion>(EstadoValidacion), Fusionado, FusionadoEn);
    }
}

public sealed class RegistroConflicto
{
    public Guid IdConflicto { get; set; }
    public Guid? IdActivo { get; set; }
    public string Estado { get; set; } = "";
    public string? Decision { get; set; }
    public Guid? IdActivoVigente { get; set; }

    public ConflictoLeido AConflicto() => new(IdConflicto, IdActivo, Estado, Decision, IdActivoVigente);
}

/// <summary>
/// Adaptador del almacén de proyecciones en PostGIS. Corre dentro de la transacción del consumidor idempotente y escribe
/// con SQL explícito para controlar los bloqueos. Orden de los bloqueos en todo el servicio, para que dos réplicas no se
/// interbloqueen: la fila de p_bandeja, los activos (por id), las filas del consolidado (por clave) y los desastres (por id).
/// </summary>
public sealed class AlmacenProyeccionesPostgis : IAlmacenProyecciones
{
    /// <summary>Tolerancia de simplificación de los municipios para el mapa, en grados (alrededor de 50 m), como en Consulta.</summary>
    public const double Tolerancia = 0.0005;

    private readonly ConsultaDanosDbContext _db;
    public AlmacenProyeccionesPostgis(ConsultaDanosDbContext db) { _db = db; }

    public async Task<EstadoActivo> BloquearActivoAsync(Guid idActivo, DateTimeOffset instante, CancellationToken ct)
    {
        // Si dos réplicas crean el mismo activo a la vez, la segunda espera a la primera y no inserta nada.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO p_evaluacion_vigente (id_activo, version_evaluacion, version_activo, origen_contenido, validada, fusionado, actualizado_al)
            VALUES ({idActivo}, 0, -1, 0, false, false, {instante}) ON CONFLICT (id_activo) DO NOTHING", ct);
        List<RegistroActivo> filas = await _db.Database.SqlQueryRaw<RegistroActivo>(
            $"SELECT {RegistroActivo.Columnas}, false AS \"Conflicto\" FROM p_evaluacion_vigente WHERE id_activo = {{0}} FOR UPDATE", idActivo).ToListAsync(ct);
        return filas.Single().AEstado();
    }

    public async Task GuardarActivosAsync(IReadOnlyList<EstadoActivo> cambiados, IReadOnlyList<(ClaveZona Clave, int Delta)> deltas,
        IReadOnlyCollection<Guid> desastres, DateTimeOffset instante, CancellationToken ct)
    {
        foreach (EstadoActivo a in cambiados.OrderBy(a => a.IdActivo))
        {
            await GuardarActivoAsync(a, instante, ct);
        }
        foreach ((ClaveZona clave, int delta) in deltas.OrderBy(d => d.Clave))
        {
            string nivel = clave.Nivel.ToString();
            if (delta > 0)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO p_dano_por_zona (id_desastre, codigo_municipio, tipo_activo, nivel_dano, cantidad, actualizado_al)
                    VALUES ({clave.IdDesastre}, {clave.CodigoMunicipio}, {clave.TipoActivo}, {nivel}, {delta}, {instante})
                    ON CONFLICT (id_desastre, codigo_municipio, tipo_activo, nivel_dano) DO UPDATE
                    SET cantidad = p_dano_por_zona.cantidad + EXCLUDED.cantidad,
                        actualizado_al = GREATEST(p_dano_por_zona.actualizado_al, EXCLUDED.actualizado_al)", ct);
            }
            else
            {
                // Un -1 solo puede venir de un activo que ya contaba: la fila existe y el check (cantidad >= 0) lo respalda.
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE p_dano_por_zona SET cantidad = cantidad + {delta}, actualizado_al = GREATEST(actualizado_al, {instante})
                    WHERE id_desastre = {clave.IdDesastre} AND codigo_municipio = {clave.CodigoMunicipio}
                      AND tipo_activo = {clave.TipoActivo} AND nivel_dano = {nivel}", ct);
            }
        }
        foreach (Guid desastre in desastres.Order())
        {
            await MarcarDesastreAsync(desastre, instante, ct);
        }
    }

    private Task GuardarActivoAsync(EstadoActivo a, DateTimeOffset instante, CancellationToken ct)
    {
        string? validacion = a.Validacion?.ToString();
        if (a.Contenido is not { } c)
        {
            return _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE p_evaluacion_vigente SET estado_validacion = {validacion}, validada = {a.Validada}, fusionado = {a.Fusionado},
                    fusionado_en = {a.FusionadoEn}, actualizado_al = GREATEST(actualizado_al, {instante})
                WHERE id_activo = {a.IdActivo}", ct);
        }
        string nivel = c.Nivel.ToString();
        short origen = (short)a.Origen;
        return _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE p_evaluacion_vigente SET id_evaluacion = {c.IdEvaluacion}, id_desastre = {c.IdDesastre}, tipo_activo = {c.TipoActivo},
                nivel_dano = {nivel}, codigo_municipio = {c.CodigoMunicipio}, ubicacion = ST_SetSRID(ST_MakePoint({c.Lon}, {c.Lat}), 4326),
                precision_m = {c.PrecisionM}, estado_evaluacion = {c.EstadoEvaluacion}, version_evaluacion = {c.VersionEvaluacion},
                version_activo = {a.VersionActivo}, origen_contenido = {origen}, estado_validacion = {validacion}, validada = {a.Validada},
                fusionado = {a.Fusionado}, fusionado_en = {a.FusionadoEn}, actualizado_al = GREATEST(actualizado_al, {instante})
            WHERE id_activo = {a.IdActivo}", ct);
    }

    public async Task<EstadoValidacion?> ValidacionDeAsync(Guid idEvaluacion, CancellationToken ct)
    {
        List<string> estado = await _db.Database.SqlQuery<string>($@"
            SELECT estado AS ""Value"" FROM p_validaciones WHERE id_evaluacion = {idEvaluacion}").ToListAsync(ct);
        return estado.Count == 0 ? null : Enum.Parse<EstadoValidacion>(estado[0]);
    }

    public Task GuardarValidacionAsync(ValidacionCambiada h, CancellationToken ct)
    {
        string estado = h.Estado.ToString();
        return _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO p_validaciones (id_evaluacion, id_activo, estado, version, validado_por, instante_validacion)
            VALUES ({h.Contenido.IdEvaluacion}, {h.IdActivo}, {estado}, {h.Contenido.VersionEvaluacion}, {h.ValidadoPor}, {h.InstanteValidacion})
            ON CONFLICT (id_evaluacion) DO UPDATE SET id_activo = EXCLUDED.id_activo, estado = EXCLUDED.estado, version = EXCLUDED.version,
                validado_por = EXCLUDED.validado_por, instante_validacion = EXCLUDED.instante_validacion
            WHERE p_validaciones.version < EXCLUDED.version", ct);
    }

    public Task RegistrarHistorialAsync(EntradaHistorial e, CancellationToken ct)
    {
        string nivel = e.Nivel.ToString();
        string? validacion = e.Validacion?.ToString();
        return _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO p_historial_activo (id_activo, id_evaluacion, version_evaluacion, origen, instante, nivel_dano, tipo_activo,
                estado_evaluacion, estado_validacion, en_conflicto, id_desastre, codigo_municipio, autor, detalle)
            VALUES ({e.IdActivo}, {e.IdEvaluacion}, {e.VersionEvaluacion}, {e.Origen}, {e.Instante}, {nivel}, {e.TipoActivo},
                {e.EstadoEvaluacion}, {validacion}, {e.EnConflicto}, {e.IdDesastre}, {e.CodigoMunicipio}, {e.Autor}, {e.Detalle})
            ON CONFLICT DO NOTHING", ct);
    }

    public Task BloquearBandejaAsync(CancellationToken ct) =>
        _db.Database.ExecuteSqlRawAsync("SELECT id FROM p_bandeja WHERE id = 1 FOR UPDATE", ct);

    public async Task<ConflictoLeido> GuardarConflictoDetectadoAsync(ConflictoDetectado h, CancellationToken ct)
    {
        // Si EV-22 llegó antes, el conflicto ya está resuelto: EV-10 solo completa el activo y el instante, sin reabrirlo.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO p_conflictos (id_conflicto, id_activo, tipo, id_evaluacion_entrante, estado, detectado_en)
            VALUES ({h.IdConflicto}, {h.IdActivo}, {h.Tipo}, {h.IdEvaluacionEntrante}, 'PENDIENTE', {h.Instante})
            ON CONFLICT (id_conflicto) DO UPDATE SET id_activo = EXCLUDED.id_activo, tipo = EXCLUDED.tipo,
                id_evaluacion_entrante = EXCLUDED.id_evaluacion_entrante, detectado_en = EXCLUDED.detectado_en", ct);
        return await ConflictoAsync(h.IdConflicto, ct);
    }

    public async Task<ConflictoLeido> GuardarConflictoResueltoAsync(ConflictoResuelto h, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO p_conflictos (id_conflicto, estado, decision, id_activo_vigente, resuelto_en)
            VALUES ({h.IdConflicto}, 'RESUELTO', {h.Decision}, {h.IdActivoVigente}, {h.Instante})
            ON CONFLICT (id_conflicto) DO UPDATE SET estado = 'RESUELTO', decision = EXCLUDED.decision,
                id_activo_vigente = EXCLUDED.id_activo_vigente, resuelto_en = EXCLUDED.resuelto_en", ct);
        return await ConflictoAsync(h.IdConflicto, ct);
    }

    private async Task<ConflictoLeido> ConflictoAsync(Guid id, CancellationToken ct) =>
        (await _db.Database.SqlQuery<RegistroConflicto>($@"
            SELECT id_conflicto AS ""IdConflicto"", id_activo AS ""IdActivo"", estado AS ""Estado"", decision AS ""Decision"",
                   id_activo_vigente AS ""IdActivoVigente""
            FROM p_conflictos WHERE id_conflicto = {id}").ToListAsync(ct)).Single().AConflicto();

    public Task RecalcularBandejaAsync(DateTimeOffset instante, CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE p_bandeja SET pendientes = c.n, mas_viejo = c.m, dato_al = GREATEST(p_bandeja.dato_al, {instante})
        FROM (SELECT count(*)::int AS n, min(detectado_en) AS m FROM p_conflictos WHERE estado = 'PENDIENTE') c
        WHERE p_bandeja.id = 1", ct);

    public async Task<Guid?> DesastreDelActivoAsync(Guid idActivo, CancellationToken ct)
    {
        List<Guid> desastre = await _db.Database.SqlQuery<Guid>($@"
            SELECT id_desastre AS ""Value"" FROM p_evaluacion_vigente WHERE id_activo = {idActivo} AND id_desastre IS NOT NULL").ToListAsync(ct);
        return desastre.Count == 0 ? null : desastre[0];
    }

    public Task MarcarDesastreAsync(Guid desastre, DateTimeOffset instante, CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO p_desastres (id_desastre, dato_al) VALUES ({desastre}, {instante})
        ON CONFLICT (id_desastre) DO UPDATE SET dato_al = GREATEST(p_desastres.dato_al, EXCLUDED.dato_al)", ct);

    public async Task GuardarCapaAsync(CapaRecibida h, CancellationToken ct)
    {
        // La geometría se simplifica una vez, al proyectar, y no en cada consulta (AD-17). Una versión vieja de la capa
        // que llegue tarde no reemplaza a la nueva.
        foreach (DivisionLeida d in h.Divisiones)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO p_municipios (codigo, nombre, codigo_departamento, geometria, version_capa, actualizado_al)
                VALUES ({d.Codigo}, {d.Nombre}, {d.CodigoPadre},
                    ST_SimplifyPreserveTopology(ST_SetSRID(ST_GeomFromGeoJSON({d.GeometriaGeoJson}::text), 4326), {Tolerancia}),
                    {h.Version}, {h.Instante})
                ON CONFLICT (codigo) DO UPDATE SET nombre = EXCLUDED.nombre, codigo_departamento = EXCLUDED.codigo_departamento,
                    geometria = EXCLUDED.geometria, version_capa = EXCLUDED.version_capa, actualizado_al = EXCLUDED.actualizado_al
                WHERE p_municipios.version_capa <= EXCLUDED.version_capa", ct);
        }
    }

    public Task GuardarTipoActivoAsync(TipoActivoActualizado h, CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO p_tipos_activo (codigo, nombre, version) VALUES ({h.Codigo}, {h.Nombre}, {h.Version})
        ON CONFLICT (codigo) DO UPDATE SET nombre = EXCLUDED.nombre, version = EXCLUDED.version
        WHERE p_tipos_activo.version < EXCLUDED.version", ct);

    public Task GuardarIntegracionAsync(EstadoIntegracion h, CancellationToken ct) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO p_integraciones (sistema, estado, desde, mensajes_en_cola) VALUES ({h.Sistema}, {h.Nuevo}, {h.Instante}, {h.MensajesEnCola})
        ON CONFLICT (sistema) DO UPDATE SET estado = EXCLUDED.estado, desde = EXCLUDED.desde, mensajes_en_cola = EXCLUDED.mensajes_en_cola
        WHERE p_integraciones.desde <= EXCLUDED.desde", ct);
}
