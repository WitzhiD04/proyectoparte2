using AdaptadorSe7;
using Plataforma.Comun.Salud;

// Adaptador de SE-7 (DA-11). La misma imagen corre en tres modos, según su primer argumento:
//   salida   consume q.salida.se7 (EV-18 y EV-20) y entrega a SE-7 con timeout, reintento y cortacircuitos (AD-05, AD-36)
//   entrada  consulta los cambios de SE-7 desde su marca de avance y los publica en EV-24 (AD-14, E-04)
//   migrar   migrador de su esquema en la base "adaptadores" (DT-19)
// Los dos primeros publican EV-23 al abrirse y al cerrarse el cortacircuitos (E-15, QA-01 b).
string modo = args.FirstOrDefault(a => a is "salida" or "entrada" or "migrar") ?? "";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
{
    InitialData = RegistroAdaptadorSe7.ValoresPorOmision(modo),
});

switch (modo)
{
    case "migrar":
        using (ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole()))
        {
            ILogger log = registros.CreateLogger("Migrador");
            try
            {
                await MigracionAdaptadorSe7.EjecutarAsync(builder.Configuration, log);
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
    case "entrada":
        builder.Services.AgregarModoEntrada(builder.Configuration);
        break;
    default:
        Console.Error.WriteLine("Uso: AdaptadorSe7 salida | entrada | migrar");
        return 2;
}

WebApplication app = builder.Build();
app.MapearSalud("adaptador-se7", modo);
await app.RunAsync();
return 0;
