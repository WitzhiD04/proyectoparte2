using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Infraestructura;
using Auditoria.Infraestructura.Persistencia;
using Plataforma.Comun.Salud;
using Plataforma.Comun.Seguridad;

// Microservicio de Auditoría (AD-21, DA-17): consume q.auditoria, guarda cada evento en un registro inmutable, detecta
// huecos en la secuencia de cada productor (ATAM R-02) y expone una consulta de solo lectura.
// Con el argumento "migrar" la imagen es el migrador (DT-19): aplica el esquema con el rol dueño y termina.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionAuditoria.EjecutarAsync(builder.Configuration, log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

builder.Services.AddControllers();
builder.Services.AgregarIdentidad(builder.Configuration);

// Aquí se conecta cada puerto con su implementación (adaptadores).
builder.Services.AgregarInfraestructura(builder.Configuration);
builder.Services.AddScoped<IConsultarRegistroEntrada, ConsultarRegistro>();
builder.Services.AddScoped<IConsultarHuecosEntrada, ConsultarHuecos>();

WebApplication app = builder.Build();

// /salud/vivo y /salud/listo (base de datos y cola del bus), para el sondeo de las réplicas (AD-24).
app.MapearSalud("auditoria");
app.MapControllers();
await app.RunAsync();
return 0;

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
