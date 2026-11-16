using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Plataforma.Comun.Eventos;

namespace Plataforma.Comun.Mensajeria;

/// <summary>Marca que el servicio publica eventos (lo registra <see cref="RegistroMensajeria.AgregarPublicacion{TContexto}"/>).</summary>
public sealed class MarcaPublicador { }

/// <summary>
/// Sondeo de preparación: el bus responde y la topología que el servicio usa está declarada (DA-03). Verifica solo lo
/// que el servicio usa, porque su usuario del bus solo tiene permiso sobre eso (mínimo privilegio): el exchange si
/// publica y su cola si consume.
/// </summary>
public sealed class SaludBus : IHealthCheck
{
    private readonly ConexionBus _bus;
    private readonly OpcionesBus _opciones;
    private readonly bool _publica;

    public SaludBus(ConexionBus bus, OpcionesBus opciones, IEnumerable<MarcaPublicador> publicador)
    {
        _bus = bus;
        _opciones = opciones;
        _publica = publicador.Any();
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext contexto, CancellationToken ct = default)
    {
        try
        {
            await _bus.VerificarTopologiaAsync(_publica ? CatalogoEventos.Exchange : null, _opciones.Cola, ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e)
        {
            return new HealthCheckResult(contexto.Registration.FailureStatus, "El bus no responde o falta la topología declarada.", e);
        }
    }
}

public static class RegistroMensajeria
{
    /// <summary>Conexión al bus, opciones y sondeo de preparación del bus.</summary>
    public static IServiceCollection AgregarBus(this IServiceCollection servicios, IConfiguration configuracion)
    {
        OpcionesBus opciones = OpcionesBus.Leer(configuracion);
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<ConexionBus>();
        servicios.AddHealthChecks().AddCheck<SaludBus>("bus", tags: ["listo"]);
        return servicios;
    }

    /// <summary>Escritor de la bandeja y relevo hacia el bus, para un servicio productor.</summary>
    public static IServiceCollection AgregarPublicacion<TContexto>(this IServiceCollection servicios) where TContexto : DbContext
    {
        servicios.AddSingleton<MarcaPublicador>();
        servicios.AddScoped<EscritorBandeja<TContexto>>();
        servicios.AddHostedService<RelevoBandeja<TContexto>>();
        return servicios;
    }

    /// <summary>Consumidor idempotente de la cola del servicio, con su adaptador de entrada.</summary>
    public static IServiceCollection AgregarConsumo<TContexto, TManejador>(this IServiceCollection servicios)
        where TContexto : DbContext
        where TManejador : class, IManejadorEvento
    {
        servicios.AddScoped<IManejadorEvento, TManejador>();
        servicios.AddHostedService<ConsumidorIdempotente<TContexto>>();
        return servicios;
    }
}
