using AdaptadorSe2;
using Microsoft.EntityFrameworkCore;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Salud;

// Adaptador de SE-2 (AD-03, DA-07): entrega a la plataforma la capa de municipios como EV-21, por su bandeja de
// salida. Aísla el formato del sistema externo: los servicios solo conocen el evento canónico (AD-09).
// Con el argumento "migrar" la imagen es el migrador de su base (DT-19).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Valores por omisión, con la menor prioridad: las variables de entorno los reemplazan.
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
{
    InitialData = new Dictionary<string, string?>
    {
        ["Bus:Usuario"] = CatalogoEventos.AdaptadorSe2,
        ["Bus:Productor"] = CatalogoEventos.AdaptadorSe2,
    },
});

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionAdaptadorSe2.EjecutarAsync(builder.Configuration, log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

OpcionesCapa opcionesCapa = new();
builder.Configuration.GetSection("Capa").Bind(opcionesCapa);
builder.Services.AddSingleton(opcionesCapa);

builder.Services.AddDbContext<AdaptadorSe2DbContext>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("AdaptadorSe2") ?? throw new InvalidOperationException("Falta ConnectionStrings:AdaptadorSe2.")));
builder.Services.AddHealthChecks().AddDbContextCheck<AdaptadorSe2DbContext>("base", tags: ["listo"]);

// Bandeja de salida y relevo hacia el bus (AD-45, DA-04). El adaptador solo publica: no tiene cola.
builder.Services.AgregarBus(builder.Configuration);
builder.Services.AgregarPublicacion<AdaptadorSe2DbContext>();
builder.Services.AddScoped<PublicacionDeCapas>();
builder.Services.AddHostedService<VigilanteDeCapas>();

WebApplication app = builder.Build();
app.MapearSalud("adaptador-se2");
await app.RunAsync();
return 0;
