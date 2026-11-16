using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Plataforma.Comun.Adaptadores;

/// <summary>Configuración de la resiliencia hacia el sistema externo, sección "Resiliencia" (DT-09).</summary>
public sealed class OpcionesResiliencia
{
    public const string Seccion = "Resiliencia";

    /// <summary>Tiempo de espera de cada intento (AD-05). Un intento que lo agota cuenta como fallo para el cortacircuitos.</summary>
    public double TiempoEsperaIntentoSegundos { get; set; } = 3;

    /// <summary>Tiempo máximo de una operación con todos sus reintentos.</summary>
    public double TiempoEsperaTotalSegundos { get; set; } = 20;

    /// <summary>Reintentos después del primer intento. Solo para operaciones idempotentes (Cuadro 5).</summary>
    public int Reintentos { get; set; } = 2;

    /// <summary>Espera base del reintento exponencial con jitter.</summary>
    public double EsperaBaseReintentoSegundos { get; set; } = 0.5;

    // Cortacircuitos (DA-11). El Cuadro 5 dice "se abre tras tres tiempos de espera agotados"; Polly mide una
    // proporción en una ventana, así que 3 llamadas como mínimo y 100 % de fallos en 30 s equivalen a "3 de 3".
    public int MinimoLlamadas { get; set; } = 3;
    public double ProporcionFallos { get; set; } = 1.0;
    public double VentanaSegundos { get; set; } = 30;

    /// <summary>Cuánto permanece abierto antes de dejar pasar una llamada de prueba (SEMIABIERTO).</summary>
    public double DuracionAperturaSegundos { get; set; } = 15;

    public static OpcionesResiliencia Leer(IConfiguration configuracion)
    {
        OpcionesResiliencia opciones = new();
        configuracion.GetSection(Seccion).Bind(opciones);
        return opciones;
    }
}

/// <summary>
/// Resiliencia de las llamadas HTTP de un adaptador a su sistema externo (AD-05, AD-37, DT-09), con
/// Microsoft.Extensions.Http.Resilience (Polly v8). De afuera hacia adentro:
/// <list type="number">
/// <item>tiempo total de la operación;</item>
/// <item>reintento exponencial con jitter, solo para métodos idempotentes (Cuadro 5): GET, HEAD, OPTIONS, PUT, DELETE y
/// TRACE según RFC 9110. POST y PATCH no se reintentan;</item>
/// <item>cortacircuitos "3 de 3 en 30 s", que notifica su apertura y su cierre a <see cref="MonitorCircuito"/> (EV-23);</item>
/// <item>tiempo de espera de cada intento.</item>
/// </list>
/// El cortacircuitos va dentro del reintento: cada intento cuenta para él, así que una entrega que agota sus tres
/// intentos por tiempo de espera abre el circuito (estímulo de QA-01).
/// </summary>
public static class RegistroResiliencia
{
    public const string NombrePipeline = "sistema-externo";

    /// <summary>Agrega la resiliencia al cliente HTTP del sistema externo y registra su <see cref="MonitorCircuito"/>.</summary>
    public static IHttpClientBuilder AgregarResilienciaAdaptador(this IHttpClientBuilder cliente, OpcionesResiliencia opciones)
    {
        cliente.AddResilienceHandler(NombrePipeline, (pipeline, contexto) =>
            Configurar(pipeline, opciones, contexto.ServiceProvider.GetRequiredService<MonitorCircuito>()));
        return cliente;
    }

    /// <summary>La configuración de la resiliencia. Pública para probarla sin servidor.</summary>
    public static void Configurar(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, OpcionesResiliencia opciones,
        MonitorCircuito monitor)
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(opciones.TiempoEsperaTotalSegundos));

        if (opciones.Reintentos > 0)
        {
            HttpRetryStrategyOptions reintento = new()
            {
                MaxRetryAttempts = opciones.Reintentos,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(opciones.EsperaBaseReintentoSegundos),
                // Si el sistema pide esperar (Retry-After) no se le hace caso: el cortacircuitos ya protege al sistema.
                ShouldRetryAfterHeader = false,
            };
            // Cuadro 5: solo se reintentan operaciones idempotentes.
            reintento.DisableFor(HttpMethod.Post, HttpMethod.Patch, HttpMethod.Connect);
            pipeline.AddRetry(reintento);
        }

        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = opciones.MinimoLlamadas,
            FailureRatio = opciones.ProporcionFallos,
            SamplingDuration = TimeSpan.FromSeconds(opciones.VentanaSegundos),
            BreakDuration = TimeSpan.FromSeconds(opciones.DuracionAperturaSegundos),
            OnOpened = _ =>
            {
                monitor.AlAbrirse();
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = _ =>
            {
                monitor.AlSemiabrirse();
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                monitor.AlCerrarse();
                return ValueTask.CompletedTask;
            },
        });

        pipeline.AddTimeout(TimeSpan.FromSeconds(opciones.TiempoEsperaIntentoSegundos));
    }
}

/// <summary>Clasifica los errores de una llamada al sistema externo.</summary>
public static class Indisponibilidad
{
    /// <summary>
    /// El error significa que el sistema externo no está disponible (transitorio), no que el mensaje sea inválido:
    /// circuito abierto, tiempo de espera agotado, error de red o respuesta 5xx/408/429.
    /// </summary>
    public static bool Es(Exception e, CancellationToken ct) => e switch
    {
        BrokenCircuitException => true,
        TimeoutRejectedException => true,
        HttpRequestException => true,
        SistemaExternoNoDisponibleException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };
}
