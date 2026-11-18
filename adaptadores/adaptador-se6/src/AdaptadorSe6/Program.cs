using AdaptadorSe6;
using Plataforma.Comun.Salud;

// Adaptador de SE-6, sistema financiero externo (DA-11). Solo salida (envío por lote, Cuadro 14):
//   salida (o sin argumento)  consume q.salida.se6 (EV-18, EV-19 y EV-20), acumula en lotes por tamaño o tiempo y
//                             entrega a SE-6 con timeout, reintento y cortacircuitos (AD-05, AD-36, AD-37)
//   migrar                    migrador de su esquema se6 en la base "adaptadores" (DT-19)
// Publica EV-23 al abrirse y al cerrarse el cortacircuitos (E-15, QA-01 b).
string? primerArg = args.FirstOrDefault(a => !a.StartsWith('-'));
string modo = primerArg ?? "salida";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
{
    InitialData = RegistroAdaptadorSe6.ValoresPorOmision(),
});

switch (modo)
{
    case "migrar":
        using (ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole()))
        {
            ILogger log = registros.CreateLogger("Migrador");
            try
            {
                await MigracionAdaptadorSe6.EjecutarAsync(builder.Configuration, log);
                return 0;
            }
            catch (Exception e)
            {
                log.LogCritical(e, "La migración falló.");
                return 1;
            }
        }
    case "salida":
        builder.Services.AgregarModoSalida(builder.Configuration);
        break;
    default:
        Console.Error.WriteLine("Uso: AdaptadorSe6 [salida | migrar]");
        return 2;
}

WebApplication app = builder.Build();
app.MapearSalud("adaptador-se6", "salida");
await app.RunAsync();
return 0;
