using SimuladorQa14;

// Simulador de QA-14: 120 evaluaciones hechas sin conexión, 5 de ellas en conflicto, enviadas en lotes de 20
// y reenviadas completas.
// Uso: bash herramientas/dotnet.sh run --project simuladores/simulador-qa14 -- --url http://localhost:5080
// Es repetible: cada corrida crea sus propios 120 activos, usa dispositivos y GUID nuevos y toma como base
// la versión descargada. OJO: escribe datos simulados en la base del servicio; córralo contra una base aparte
// para no mezclarlos con los que llegan de la app.

string url = "http://localhost:5080";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--url")
    {
        url = args[i + 1];
    }
}

Console.WriteLine($"Simulador QA-14 contra {url}");
var simulador = new Simulador(encabezados =>
{
    var cliente = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
    foreach (var (nombre, valor) in encabezados)
    {
        cliente.DefaultRequestHeaders.Add(nombre, valor);
    }
    return cliente;
}, Console.Out);

var informe = await simulador.EjecutarAsync();
return informe.Pasa ? 0 : 1;
