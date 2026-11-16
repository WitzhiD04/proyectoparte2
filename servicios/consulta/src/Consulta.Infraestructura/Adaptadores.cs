using System.Text.Json;
using System.Text.Json.Nodes;
using Consulta.Aplicacion;
using Consulta.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;
using StackExchange.Redis;

namespace Consulta.Infraestructura;

/// <summary>
/// Caché en Redis (DT-06, AD-17). La versión del mapa es un contador: subirlo invalida de una vez todas las respuestas
/// armadas, que además expiran solas. Las claves llevan el prefijo del servicio para no mezclarse con las de Consulta de
/// Daños en el mismo motor (E-21).
/// </summary>
public sealed class CacheRedis : ICacheConsulta
{
    private const string ClaveVersion = "consulta:mapa:version";
    private readonly IConnectionMultiplexer _redis;
    public CacheRedis(IConnectionMultiplexer redis) { _redis = redis; }

    public async Task<string?> LeerAsync(string clave, CancellationToken ct)
    {
        RedisValue valor = await _redis.GetDatabase().StringGetAsync(clave);
        return valor.HasValue ? valor.ToString() : null;
    }

    public Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct) =>
        vigencia is null
            ? _redis.GetDatabase().StringSetAsync(clave, valor)
            : _redis.GetDatabase().StringSetAsync(clave, valor, new Expiration(vigencia.Value));

    public async Task<long> VersionMapaAsync(CancellationToken ct)
    {
        RedisValue valor = await _redis.GetDatabase().StringGetAsync(ClaveVersion);
        return valor.HasValue ? (long)valor : 0;
    }

    public Task InvalidarMapaAsync(CancellationToken ct) => _redis.GetDatabase().StringIncrementAsync(ClaveVersion);
}

/// <summary>
/// La caché no forma parte de la preparación del servicio: si Redis cae, el mapa se arma desde la base (degradación
/// controlada, CON-6). Por eso este sondeo informa "degradado" y no "no listo".
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

/// <summary>El mapa como GeoJSON FeatureCollection: una zona por desastre, con su nivel y estado para colorearla.</summary>
public sealed class FormatoMapaGeoJson : IFormatoMapa
{
    public string Serializar(IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)> zonas, DateTimeOffset? datoAl, bool degradado)
    {
        JsonArray features = new();
        foreach ((FilaTablero d, ZonaLeida z) in zonas.Where(x => x.Zona.GeometriaGeoJson is not null))
        {
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = JsonNode.Parse(z.GeometriaGeoJson!),
                ["properties"] = new JsonObject
                {
                    ["idDesastre"] = d.Id, ["nombre"] = d.Nombre, ["tipo"] = d.Tipo, ["severidad"] = d.Severidad, ["estado"] = d.Estado,
                    ["zonaVersion"] = z.Version, ["vigenteDesde"] = z.VigenteDesde, ["municipios"] = new JsonArray(z.CodigosMunicipio.Select(c => (JsonNode?)c).ToArray()),
                    ["datoAl"] = d.DatoAl,
                },
            });
        }
        return new JsonObject { ["type"] = "FeatureCollection", ["datoAl"] = datoAl, ["degradado"] = degradado, ["features"] = features }.ToJsonString();
    }

    public string MarcarDegradado(string mapa)
    {
        JsonObject objeto = JsonNode.Parse(mapa)!.AsObject();
        objeto["degradado"] = true;
        return objeto.ToJsonString();
    }
}

/// <summary>Adaptador de entrada de q.consulta: traduce cada evento al hecho del modelo de lectura.</summary>
public sealed class ManejadorEventosConsulta : IManejadorEvento
{
    private readonly ActualizarProyecciones _actualizar;
    private readonly ILogger<ManejadorEventosConsulta> _log;

    public ManejadorEventosConsulta(ActualizarProyecciones actualizar, ILogger<ManejadorEventosConsulta> log)
    {
        _actualizar = actualizar;
        _log = log;
    }

    public Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        DateTimeOffset t = sobre.InstanteEmision;
        switch (sobre.Tipo)
        {
            case "EV-03":
                DesastreCreadoV1 c = sobre.LeerDatos<DesastreCreadoV1>();
                return _actualizar.AplicarAsync(p => p.AplicarAsync(new DesastreRegistrado(c.IdDesastre, c.Tipo, c.Nombre, c.Descripcion,
                    c.Severidad.ToString(), c.Estado.ToString(), c.OrganismoRegistra, c.Responsable, c.InstanteInicio, c.Ubicacion.Lat, c.Ubicacion.Lon,
                    c.VersionDesastre, c.OrganismoRegistra, t), ct), true, ct);
            case "EV-04":
                SeveridadCambiadaV1 s = sobre.LeerDatos<SeveridadCambiadaV1>();
                return _actualizar.AplicarAsync(p => p.AplicarAsync(new SeveridadCambiada(s.IdDesastre, s.SeveridadAnterior.ToString(),
                    s.SeveridadNueva.ToString(), s.Version, s.Autor, s.Motivo, t), ct), true, ct);
            case "EV-05":
                ZonaActualizadaV1 z = sobre.LeerDatos<ZonaActualizadaV1>();
                return _actualizar.AplicarAsync(p => p.AplicarAsync(new ZonaActualizada(z.IdDesastre, z.ZonaVersion,
                    z.Geometria is { ValueKind: JsonValueKind.Object } g ? g.GetRawText() : null, z.CodigosMunicipio, z.FuenteGeometria, z.Autor, t), ct), true, ct);
            case "EV-06":
                EstadoDesastreCambiadoV1 e = sobre.LeerDatos<EstadoDesastreCambiadoV1>();
                return _actualizar.AplicarAsync(p => p.AplicarAsync(new EstadoCambiado(e.IdDesastre, e.EstadoAnterior.ToString(),
                    e.EstadoNuevo.ToString(), e.VersionDesastre, e.Autor, t), ct), true, ct);
            case "EV-27":
                OrganismosDesastreCambiadosV1 o = sobre.LeerDatos<OrganismosDesastreCambiadosV1>();
                return _actualizar.AplicarAsync(p => p.AplicarAsync(new OrganismosCambiados(o.IdDesastre, o.OrganismoResponsable,
                    o.Participantes.Select(x => (x.Organismo, x.Responsabilidad)).ToList(), o.Version, o.Autor, t), ct), false, ct);
            case "EV-23":
                EstadoIntegracionCambiadoV1 i = sobre.LeerDatos<EstadoIntegracionCambiadoV1>();
                return _actualizar.AplicarAsync(p => p.AplicarAsync(new Consulta.Aplicacion.EstadoIntegracion(i.SistemaExterno, i.EstadoAnterior.ToString(),
                    i.EstadoNuevo.ToString(), i.Instante, i.MensajesEnCola), ct), false, ct);
            case "EV-21":
                CapaGeograficaActualizadaV1 capa = sobre.LeerDatos<CapaGeograficaActualizadaV1>();
                if (capa.Capa != CapaGeografica.MUNICIPIOS)
                {
                    return Task.CompletedTask;
                }
                return _actualizar.AplicarAsync(p => p.AplicarCapaAsync(capa.Divisiones.Select(d => new DivisionLeida(d.Codigo, d.Nombre, d.CodigoPadre,
                    d.Geometria.GetRawText())).ToList(), capa.VersionCapa, ct), false, ct);
            default:
                _log.LogDebug("Consulta no proyecta {Tipo}; se ignora.", sobre.Tipo);
                return Task.CompletedTask;
        }
    }
}

public static class Registro
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        string cadena = configuracion.GetConnectionString("Consulta") ?? throw new InvalidOperationException("Falta ConnectionStrings:Consulta.");
        servicios.AddDbContext<ConsultaDbContext>(o => o.UseNpgsql(cadena, n => n.UseNetTopologySuite()));
        servicios.AddHealthChecks().AddDbContextCheck<ConsultaDbContext>("base", tags: ["listo"]).AddCheck<SaludCache>("cache", tags: ["listo"]);

        // Redis: AbortOnConnectFail=false para que el servicio arranque y degrade si la caché no está (CON-6).
        ConfigurationOptions redis = ConfigurationOptions.Parse(configuracion["Redis:Conexion"] ?? "localhost:6379");
        redis.AbortOnConnectFail = false;
        redis.ConnectTimeout = 2000;
        redis.SyncTimeout = 1000;
        servicios.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        servicios.AddScoped<IProyecciones, ProyeccionesPostgis>();
        servicios.AddScoped<ILecturas, LecturasPostgis>();
        servicios.AddSingleton<ICacheConsulta, CacheRedis>();
        servicios.AddSingleton<IFormatoMapa, FormatoMapaGeoJson>();

        servicios.AgregarBus(configuracion);
        servicios.AgregarConsumo<ConsultaDbContext, ManejadorEventosConsulta>();
        return servicios;
    }
}

public static class MigracionConsulta
{
    public const string RolAplicacion = "consulta_app";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = configuracion.GetConnectionString("ConsultaDueno") ?? throw new InvalidOperationException("Falta ConnectionStrings:ConsultaDueno.");
        string clave = configuracion["Migracion:ClaveConsultaApp"] is { Length: > 0 } c ? c : throw new InvalidOperationException("Falta Migracion:ClaveConsultaApp.");
        return Migrador.EjecutarAsync(dueno, [new RolBase(RolAplicacion, clave)],
            [new BaseDelServicio("consulta", () => new ConsultaDbContext(
                new DbContextOptionsBuilder<ConsultaDbContext>().UseNpgsql(dueno, n => n.UseNetTopologySuite()).Options))],
            sembrar: false, log, ct);
    }
}
