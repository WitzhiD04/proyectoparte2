using Microsoft.Extensions.DependencyInjection.Extensions;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace AdaptadorSe7;

/// <summary>Cableado de los dos modos del adaptador. Lo usan Program.cs y las pruebas de integración.</summary>
public static class RegistroAdaptadorSe7
{
    public const string ColaSalida = "q.salida.se7";
    public const string FlujoEntrada = "se7.cambios";

    /// <summary>
    /// Valores por omisión, con la menor prioridad (las variables de entorno los reemplazan). Prefetch 1: la salida toma
    /// un solo mensaje a la vez, de modo que con SE-7 caído los demás quedan listos en la cola (AD-36) y KEDA los cuenta.
    /// </summary>
    public static Dictionary<string, string?> ValoresPorOmision(string modo) => new()
    {
        ["Bus:Usuario"] = CatalogoEventos.AdaptadorSe7,
        ["Bus:Productor"] = CatalogoEventos.AdaptadorSe7,
        ["Bus:Prefetch"] = "1",
        ["Bus:Cola"] = modo == "salida" ? ColaSalida : null,
        ["Entrada:Flujo"] = FlujoEntrada,
    };

    /// <summary>Salida: consume q.salida.se7 (EV-18 y EV-20) y entrega a SE-7.</summary>
    public static IServiceCollection AgregarModoSalida(this IServiceCollection servicios, IConfiguration configuracion)
    {
        AgregarComun(servicios, configuracion);
        servicios.AgregarSalidaAdaptador<AdaptadorSe7DbContext, ClienteSe7>(configuracion);
        return servicios;
    }

    /// <summary>Entrada: consulta periódica de los cambios de SE-7 desde su marca de avance, publicados en EV-24.</summary>
    public static IServiceCollection AgregarModoEntrada(this IServiceCollection servicios, IConfiguration configuracion)
    {
        AgregarComun(servicios, configuracion);
        servicios.AgregarEntradaAdaptador<AdaptadorSe7DbContext, CambioObraSe7, ClienteSe7>(configuracion);
        return servicios;
    }

    private static void AgregarComun(IServiceCollection servicios, IConfiguration configuracion)
    {
        string cadena = configuracion.GetConnectionString("AdaptadorSe7")
                        ?? throw new InvalidOperationException("Falta ConnectionStrings:AdaptadorSe7.");
        servicios.AddDbContext<AdaptadorSe7DbContext>(o => MigracionAdaptadorSe7.Usar(o, cadena));
        servicios.AddHealthChecks().AddDbContextCheck<AdaptadorSe7DbContext>("base", tags: ["listo"]);

        // Bandeja de salida y relevo (AD-45): EV-23 en los dos modos y EV-24 en la entrada.
        servicios.AgregarBus(configuracion);
        servicios.AgregarPublicacion<AdaptadorSe7DbContext>();
        // EV-23 informa cuántos mensajes esperan en la cola de salida, en los dos modos (el usuario del bus puede leerla).
        servicios.AgregarEstadoIntegracion<AdaptadorSe7DbContext>(TraduccionSe7.Sistema, ColaSalida);

        OpcionesSe7 se7 = new();
        configuracion.GetSection(OpcionesSe7.Seccion).Bind(se7);
        servicios.AddSingleton(se7);
        servicios.TryAddSingleton(TimeProvider.System);
        servicios.AddHttpClient<ClienteSe7>(c => c.BaseAddress = new Uri(se7.Url.EndsWith('/') ? se7.Url : se7.Url + "/"))
            .AgregarResilienciaAdaptador(OpcionesResiliencia.Leer(configuracion));
    }
}
