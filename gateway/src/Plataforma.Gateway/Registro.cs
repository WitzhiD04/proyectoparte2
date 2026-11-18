using Plataforma.Comun.Seguridad;
using Plataforma.Gateway.Catalogo;
using Plataforma.Gateway.Correlacion;
using Plataforma.Gateway.Prioridad;
using Plataforma.Gateway.Seguridad;
using Yarp.ReverseProxy.Transforms;

namespace Plataforma.Gateway;

/// <summary>Parámetros de las conexiones del gateway hacia los servicios.</summary>
public sealed class OpcionesConexiones
{
    public const string Seccion = "Gateway:Conexiones";

    /// <summary>
    /// Vida máxima de una conexión con un servicio. Al vencer se abre otra y el nombre del servicio se vuelve a
    /// resolver: si compose o Kubernetes recrean un contenedor y le cambian la IP, el gateway lo encuentra sin
    /// reiniciarse (con nginx hubo que recrear <c>web</c>, A1b).
    /// </summary>
    public TimeSpan VidaConexion { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan InactividadConexion { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan EsperaConexion { get; set; } = TimeSpan.FromSeconds(5);
}

public static class Registro
{
    /// <summary>Encabezados de identidad de la transición: el gateway nunca los reenvía (DA-15; solo vale el JWT).</summary>
    public static readonly string[] EncabezadosDeIdentidad =
    [
        IdentidadPorEncabezados.Usuario, IdentidadPorEncabezados.Roles, IdentidadPorEncabezados.Organizacion,
        IdentidadPorEncabezados.Ambito, IdentidadPorEncabezados.Territorio, "X-Rol",
    ];

    public static IServiceCollection AgregarGateway(this IServiceCollection servicios, IConfiguration configuracion)
    {
        // R-07: si el catálogo tiene una ruta de modificación sin regla de dominio, el gateway no arranca.
        CatalogoRutas.ExigirValido(configuracion);

        // El mismo validador que los servicios: mismo emisor, audiencia y llaves en caché (DA-15).
        servicios.AddSingleton(new OpcionesIdentidadJwt
        {
            Emisor = configuracion["Identidad:Emisor"] ?? OpcionesIdentidadJwt.EmisorPorOmision,
            Audiencia = configuracion["Identidad:Audiencia"] ?? OpcionesIdentidadJwt.AudienciaPorOmision,
            Metadatos = configuracion["Identidad:Metadatos"] ?? OpcionesIdentidadJwt.MetadatosPorOmision,
        });
        servicios.AddSingleton<ValidadorJwt>();

        OpcionesPrioridad prioridad = configuracion.GetSection(OpcionesPrioridad.Seccion).Get<OpcionesPrioridad>() ?? new();
        servicios.AddSingleton(prioridad);
        servicios.AddSingleton<LimitadorPorClase>();
        servicios.AddSingleton<RespuestasGuardadas>();

        OpcionesConexiones conexiones = configuracion.GetSection(OpcionesConexiones.Seccion).Get<OpcionesConexiones>() ?? new();
        servicios.AddHealthChecks();
        servicios.AddReverseProxy()
            .LoadFromConfig(configuracion.GetSection(CatalogoRutas.Seccion))
            .ConfigureHttpClient((_, manejador) =>
            {
                manejador.PooledConnectionLifetime = conexiones.VidaConexion;
                manejador.PooledConnectionIdleTimeout = conexiones.InactividadConexion;
                manejador.ConnectTimeout = conexiones.EsperaConexion;
            })
            .AddTransforms(contexto =>
            {
                foreach (string encabezado in EncabezadosDeIdentidad)
                {
                    contexto.AddRequestHeaderRemove(encabezado);
                }
            });
        return servicios;
    }

    public static WebApplication UsarGateway(this WebApplication app)
    {
        app.UseMiddleware<CorrelacionPeticiones>();
        app.MapearSaludGateway();
        // Una ruta de la API que no está en el catálogo no existe (y no cae en la consola).
        app.Map("/v1/{**resto}", (HttpContext contexto) =>
            AutorizacionNivel1.Responder(contexto, StatusCodes.Status404NotFound, "RUTA_DESCONOCIDA", "La ruta no existe."));
        app.MapReverseProxy(tuberia =>
        {
            tuberia.UseMiddleware<AutorizacionNivel1>();
            tuberia.UseMiddleware<Priorizacion>();
        });
        return app;
    }

    private static void MapearSaludGateway(this WebApplication app) =>
        Plataforma.Comun.Salud.SaludPlataforma.MapearSalud(app, "gateway");
}
