using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Infraestructura.Archivos;
using EvaluacionDanos.Infraestructura.Mensajeria;
using EvaluacionDanos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Mensajeria;

namespace EvaluacionDanos.Infraestructura;

public sealed class RelojDelSistema : IReloj
{
    /// <summary>UTC truncado a microsegundos, la precisión de PostgreSQL.</summary>
    public DateTime Ahora()
    {
        long ticks = DateTime.UtcNow.Ticks;
        return new DateTime(ticks - ticks % 10, DateTimeKind.Utc);
    }
}

public sealed class BitacoraSincronizacion(ILogger<BitacoraSincronizacion> log) : IBitacoraSincronizacion
{
    public void ChoqueDeConcurrencia(Guid? idOperacion, Exception error) =>
        log.LogInformation("Choque de concurrencia en la operación {IdOperacion} ({Tipo}); se reevalúa.",
            idOperacion, error.GetType().Name);

    public void OperacionFallida(Guid? idOperacion, Exception error) =>
        log.LogError(error, "No se pudo procesar la operación {IdOperacion}; se corta el lote", idOperacion);
}

/// <summary>
/// Sondeo de preparación: la capa de municipios ya llegó completa (EV-21). Sin ella no se puede asignar el municipio
/// que EV-09 exige, así que la réplica no recibe tráfico hasta tenerla (decisión documentada en el README).
/// </summary>
public sealed class SaludCapaGeografica(IServiceScopeFactory ambitos) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext contexto, CancellationToken ct = default)
    {
        try
        {
            using IServiceScope ambito = ambitos.CreateScope();
            bool cargada = await ambito.ServiceProvider.GetRequiredService<IAlmacenSincronizacion>().CapaDeMunicipiosCargadaAsync(ct);
            return cargada
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(contexto.Registration.FailureStatus, "Aún no llega completa la capa de municipios (EV-21).");
        }
        catch (Exception e)
        {
            return new HealthCheckResult(contexto.Registration.FailureStatus, "No se pudo consultar la capa de municipios.", e);
        }
    }
}

/// <summary>Conecta cada puerto con su adaptador.</summary>
public static class Registro
{
    public const string CadenaConexion = "EvaluacionDanos";

    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddSingleton<IReloj, RelojDelSistema>();
        servicios.AddSingleton<ICodificador, CodificadorJson>();
        servicios.AddSingleton<MemoriaCapa>();
        servicios.AddSingleton(proveedor => LeerParametros(proveedor.GetRequiredService<IConfiguration>()));
        servicios.AddScoped<IBitacoraSincronizacion, BitacoraSincronizacion>();

        // La cadena se lee al crear cada DbContext (no al arrancar), así las pruebas pueden apuntar a otra base.
        servicios.AddDbContext<EvaluacionDanosDbContext>((proveedor, o) => o.UseNpgsql(
            proveedor.GetRequiredService<IConfiguration>().GetConnectionString(CadenaConexion)
                ?? throw new InvalidOperationException($"Falta ConnectionStrings:{CadenaConexion}."),
            npgsql => npgsql.UseNetTopologySuite()));
        servicios.AddHealthChecks()
            .AddDbContextCheck<EvaluacionDanosDbContext>("base", tags: ["listo"])
            .AddCheck<SaludCapaGeografica>("capa-municipios", tags: ["listo"]);

        servicios.AddHttpContextAccessor();
        servicios.AddScoped<AlmacenEvaluacionesEf>();
        servicios.AddScoped<IAlmacenSincronizacion>(p => p.GetRequiredService<AlmacenEvaluacionesEf>());
        servicios.AddScoped<IAlmacenEvaluaciones>(p => p.GetRequiredService<AlmacenEvaluacionesEf>());
        servicios.AddScoped<IConsultasEvaluaciones, ConsultasEf>();
        servicios.AddScoped<ICopiasLocales, CopiasLocalesEf>();
        servicios.AddScoped<IPublicadorHechos, PublicadorHechosBandeja>();
        servicios.AddSingleton<IAlmacenEvidencias, AlmacenEvidenciasMinio>();

        // Bus: bandeja de salida con su relevo (AD-45, DA-04) y consumidor idempotente de q.evaluacion-danos (DA-03).
        servicios.AgregarBus(configuracion);
        servicios.AgregarPublicacion<EvaluacionDanosDbContext>();
        servicios.AgregarConsumo<EvaluacionDanosDbContext, ManejadorEventosEntrantes>();
        return servicios;
    }

    /// <summary>Las mismas secciones de configuración del prototipo: Duplicados, Sincronizacion y Evidencias.</summary>
    public static Parametros LeerParametros(IConfiguration configuracion)
    {
        Parametros porOmision = new();
        return new Parametros
        {
            DistanciaDuplicadoMetros = configuracion.GetValue("Duplicados:DistanciaMetros", porOmision.DistanciaDuplicadoMetros),
            VentanaDuplicadoHoras = configuracion.GetValue("Duplicados:VentanaHoras", porOmision.VentanaDuplicadoHoras),
            MaxOperacionesPorLote = configuracion.GetValue("Sincronizacion:MaxOperacionesPorLote", porOmision.MaxOperacionesPorLote),
            TamanoMaximoEvidenciaMb = configuracion.GetValue("Evidencias:TamanoMaximoMb", porOmision.TamanoMaximoEvidenciaMb),
            MargenDescargaSegundos = configuracion.GetValue("Sincronizacion:MargenDescargaSegundos", porOmision.MargenDescargaSegundos),
        };
    }
}
