using ConsultaDanos.Aplicacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;
using StackExchange.Redis;

namespace ConsultaDanos.Infraestructura;

public static class Registro
{
    /// <summary>Conecta cada puerto con su adaptador: PostGIS, Redis, el formato y el consumo de q.consulta-danos.</summary>
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        string cadena = configuracion.GetConnectionString("ConsultaDanos")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:ConsultaDanos.");
        servicios.AddSingleton<InterceptorInvalidacion>();
        servicios.AddDbContext<ConsultaDanosDbContext>((sp, o) => o
            .UseNpgsql(cadena, n => n.UseNetTopologySuite())
            .AddInterceptors(sp.GetRequiredService<InterceptorInvalidacion>()));
        servicios.AddHealthChecks()
            .AddDbContextCheck<ConsultaDanosDbContext>("base", tags: ["listo"])
            .AddCheck<SaludCache>("cache", tags: ["listo"]);

        // Redis (CON-6): el servicio arranca sin la caché y, mientras no esté, cada operación falla en el acto en lugar de
        // esperar en cola (FailFast), así el mapa se arma desde la base sin sumar la espera de Redis a cada respuesta.
        ConfigurationOptions redis = ConfigurationOptions.Parse(configuracion["Redis:Conexion"] ?? "localhost:6379");
        redis.AbortOnConnectFail = false;
        redis.ConnectTimeout = 2000;
        redis.SyncTimeout = 1000;
        redis.AsyncTimeout = 1000;
        redis.BacklogPolicy = BacklogPolicy.FailFast;
        servicios.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        servicios.AddScoped<IAlmacenProyecciones, AlmacenProyeccionesPostgis>();
        servicios.AddScoped<ILecturasDanos, LecturasPostgis>();
        servicios.AddScoped<IInvalidacionMapa, InvalidacionAlConfirmar>();
        servicios.AddSingleton<ICacheDanos, CacheRedis>();
        servicios.AddSingleton<IFormatoDanos, FormatoJson>();

        servicios.AgregarBus(configuracion);
        servicios.AgregarConsumo<ConsultaDanosDbContext, ManejadorEventosConsultaDanos>();
        return servicios;
    }
}

/// <summary>
/// Despliegue del esquema (DT-19): crea el rol consulta_danos_app y aplica las migraciones, que traen los permisos de
/// mínimo privilegio. Corre con el argumento "migrar" y la cadena del dueño.
/// </summary>
public static class MigracionConsultaDanos
{
    public const string RolAplicacion = "consulta_danos_app";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = configuracion.GetConnectionString("ConsultaDanosDueno")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:ConsultaDanosDueno.");
        string clave = configuracion["Migracion:ClaveConsultaDanosApp"] is { Length: > 0 } c
            ? c : throw new InvalidOperationException("Falta Migracion:ClaveConsultaDanosApp.");
        return Migrador.EjecutarAsync(dueno, [new RolBase(RolAplicacion, clave)],
            [new BaseDelServicio("consulta_danos", () => new ConsultaDanosDbContext(
                new DbContextOptionsBuilder<ConsultaDanosDbContext>().UseNpgsql(dueno, n => n.UseNetTopologySuite()).Options))],
            sembrar: false, log, ct);
    }
}
