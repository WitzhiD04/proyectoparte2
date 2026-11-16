using System.Data.Common;
using ConsultaDanos.Aplicacion;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ConsultaDanos.Infraestructura;

/// <summary>
/// Caché en Redis (DT-06, AD-17, E-21). Comparte el motor con Consulta, con su propio espacio de claves
/// (<c>consulta-danos:</c>, AD-01). Las versiones son contadores: subirlos invalida de una vez las respuestas armadas de
/// un desastre (o de todos), que además expiran solas.
/// </summary>
public sealed class CacheRedis : ICacheDanos
{
    public const string Prefijo = "consulta-danos:";
    private const string VersionGlobal = Prefijo + "version:global";
    private readonly IConnectionMultiplexer _redis;
    public CacheRedis(IConnectionMultiplexer redis) { _redis = redis; }

    private static string Version(Guid desastre) => $"{Prefijo}version:{desastre}";

    public async Task<string?> LeerAsync(string clave, CancellationToken ct)
    {
        RedisValue valor = await _redis.GetDatabase().StringGetAsync(Prefijo + clave);
        return valor.HasValue ? valor.ToString() : null;
    }

    public async Task<IReadOnlyList<string?>> LeerVariasAsync(IReadOnlyList<string> claves, CancellationToken ct)
    {
        RedisValue[] valores = await _redis.GetDatabase().StringGetAsync(claves.Select(c => (RedisKey)(Prefijo + c)).ToArray());
        return valores.Select(v => v.HasValue ? v.ToString() : null).ToList();
    }

    public Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct) =>
        vigencia is null
            ? _redis.GetDatabase().StringSetAsync(Prefijo + clave, valor)
            : _redis.GetDatabase().StringSetAsync(Prefijo + clave, valor, new Expiration(vigencia.Value));

    public async Task<(long Global, long Desastre)> VersionesAsync(Guid desastre, CancellationToken ct)
    {
        RedisValue[] v = await _redis.GetDatabase().StringGetAsync([VersionGlobal, Version(desastre)]);
        return (v[0].HasValue ? (long)v[0] : 0, v[1].HasValue ? (long)v[1] : 0);
    }

    public async Task InvalidarAsync(IReadOnlyCollection<Guid> desastres, bool global, CancellationToken ct)
    {
        IDatabase db = _redis.GetDatabase();
        List<Task> pendientes = desastres.Select(d => (Task)db.StringIncrementAsync(Version(d))).ToList();
        if (global) pendientes.Add(db.StringIncrementAsync(VersionGlobal));
        await Task.WhenAll(pendientes);
    }
}

/// <summary>
/// La caché no forma parte de la preparación del servicio: si Redis cae, las respuestas se arman desde la base
/// (degradación controlada, CON-6). Por eso este sondeo informa "degradado" y no "no listo".
/// </summary>
public sealed class SaludCache : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    public SaludCache(IConnectionMultiplexer redis) { _redis = redis; }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext contexto, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception e)
        {
            return HealthCheckResult.Degraded("La caché no responde: el mapa se arma desde la base.", e);
        }
    }
}

/// <summary>Los desastres cuyas respuestas hay que invalidar cuando se confirme la transacción en curso.</summary>
public sealed class InvalidacionesPendientes
{
    private readonly HashSet<Guid> _desastres = [];
    private bool _global;

    public void Agregar(IEnumerable<Guid> desastres) => _desastres.UnionWith(desastres);
    public void AgregarGlobal() => _global = true;

    public (IReadOnlyCollection<Guid> Desastres, bool Global) Tomar()
    {
        (IReadOnlyCollection<Guid>, bool) r = (_desastres.ToList(), _global);
        _desastres.Clear();
        _global = false;
        return r;
    }
}

/// <summary>Adaptador del puerto: anota las invalidaciones en la unidad de trabajo del evento.</summary>
public sealed class InvalidacionAlConfirmar : IInvalidacionMapa
{
    private readonly ConsultaDanosDbContext _db;
    public InvalidacionAlConfirmar(ConsultaDanosDbContext db) { _db = db; }

    public void Programar(IReadOnlyCollection<Guid> desastres) => _db.Invalidaciones.Agregar(desastres);
    public void ProgramarGlobal() => _db.Invalidaciones.AgregarGlobal();
}

/// <summary>
/// Sube las versiones de la caché recién confirmada la transacción del consumidor. Si se subieran antes, una consulta
/// concurrente podría leer la base vieja y guardar su respuesta bajo la versión nueva, que quedaría así hasta el TTL.
/// Si Redis no responde, el evento no se pierde (ya está confirmado) y las respuestas viejas expiran con su TTL corto.
/// </summary>
public sealed class InterceptorInvalidacion : DbTransactionInterceptor
{
    private readonly ICacheDanos _cache;
    private readonly ILogger<InterceptorInvalidacion> _log;

    public InterceptorInvalidacion(ICacheDanos cache, ILogger<InterceptorInvalidacion> log)
    {
        _cache = cache;
        _log = log;
    }

    public override async Task TransactionCommittedAsync(DbTransaction transaccion, TransactionEndEventData datos, CancellationToken ct = default)
    {
        if (datos.Context is not ConsultaDanosDbContext db)
        {
            return;
        }
        (IReadOnlyCollection<Guid> desastres, bool global) = db.Invalidaciones.Tomar();
        if (desastres.Count == 0 && !global)
        {
            return;
        }
        try
        {
            await _cache.InvalidarAsync(desastres, global, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(e, "No se pudo invalidar la caché de {Desastres} desastres; las respuestas viejas expiran con su TTL.", desastres.Count);
        }
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaccion, TransactionEndEventData datos, CancellationToken ct = default)
    {
        (datos.Context as ConsultaDanosDbContext)?.Invalidaciones.Tomar();
        return Task.CompletedTask;
    }
}
