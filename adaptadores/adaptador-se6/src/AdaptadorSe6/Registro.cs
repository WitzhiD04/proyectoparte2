using Microsoft.Extensions.DependencyInjection.Extensions;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace AdaptadorSe6;

/// <summary>Cableado del adaptador de SE-6. Lo usan Program.cs y las pruebas de integración.</summary>
public static class RegistroAdaptadorSe6
{
    public const string ColaSalida = "q.salida.se6";

    /// <summary>Valores por omisión, con la menor prioridad (las variables de entorno los reemplazan).</summary>
    public static Dictionary<string, string?> ValoresPorOmision() => new()
    {
        ["Bus:Usuario"] = CatalogoEventos.AdaptadorSe6,
        ["Bus:Productor"] = CatalogoEventos.AdaptadorSe6,
        ["Bus:Cola"] = ColaSalida,
    };

    /// <summary>Salida por lotes: consume q.salida.se6 (EV-18, EV-19 y EV-20) y entrega lotes a SE-6.</summary>
    public static IServiceCollection AgregarModoSalida(this IServiceCollection servicios, IConfiguration configuracion)
    {
        string cadena = configuracion.GetConnectionString("AdaptadorSe6")
                        ?? throw new InvalidOperationException("Falta ConnectionStrings:AdaptadorSe6.");
        servicios.AddDbContext<AdaptadorSe6DbContext>(o => MigracionAdaptadorSe6.Usar(o, cadena));
        servicios.AddHealthChecks().AddDbContextCheck<AdaptadorSe6DbContext>("base", tags: ["listo"]);

        // Bandeja de salida y relevo (AD-45): EV-23 al abrirse y al cerrarse el cortacircuitos.
        servicios.AgregarBus(configuracion);
        servicios.AgregarPublicacion<AdaptadorSe6DbContext>();
        servicios.AgregarEstadoIntegracion<AdaptadorSe6DbContext>(TraduccionSe6.Sistema, ColaSalida);

        OpcionesSe6 se6 = OpcionesSe6.Leer(configuracion);
        servicios.AddSingleton(se6);
        servicios.AddSingleton(OpcionesSalida.Leer(configuracion));
        servicios.TryAddSingleton(TimeProvider.System);

        servicios.AddHttpClient<ClienteSe6>(c => c.BaseAddress = new Uri(se6.Url.EndsWith('/') ? se6.Url : se6.Url + "/"))
            .AgregarResilienciaAdaptador(OpcionesResiliencia.Leer(configuracion));

        servicios.AddScoped<ISalidaLotesSe6>(sp => sp.GetRequiredService<ClienteSe6>());
        servicios.AddScoped<ISalidaAdaptador>(sp => sp.GetRequiredService<ClienteSe6>());
        servicios.AddScoped<ISondaSistemaExterno>(sp => sp.GetRequiredService<ClienteSe6>());
        servicios.AddScoped<ManejadorLotesSe6>();
        servicios.AddSingleton<CompuertaLotesSe6>();
        servicios.AddHostedService(sp => sp.GetRequiredService<CompuertaLotesSe6>());
        return servicios;
    }
}
