using SimuladorQa14;

// Simulador de QA-14: 120 evaluaciones hechas sin conexión, 5 de ellas en conflicto, enviadas en lotes de 20
// y reenviadas completas.
// Uso: bash herramientas/dotnet.sh run --project simuladores/simulador-qa14 -- --url http://localhost:5080
// A través del gateway, con tokens de Keycloak (cliente movil, solo para pruebas):
//      dotnet run --project simuladores/simulador-qa14 -- --url http://localhost:8080 --keycloak http://localhost:8180
// Con --keycloak, el evaluador es evaluador.armenia y la bandeja de revisión la lee operador.danos; el gateway
// descarta los encabezados X-Usuario, X-Roles y X-Organizacion y deja pasar X-Dispositivo.
// Es repetible: cada corrida crea sus propios 120 activos, usa dispositivos y GUID nuevos y toma como base
// la versión descargada. OJO: escribe datos simulados en la base del servicio; córralo contra una base aparte
// para no mezclarlos con los que llegan de la app.

string url = "http://localhost:5080";
string? keycloak = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--url")
    {
        url = args[i + 1];
    }
    else if (args[i] == "--keycloak")
    {
        keycloak = args[i + 1].TrimEnd('/');
    }
}

Console.WriteLine($"Simulador QA-14 contra {url}" + (keycloak is null ? "" : $" con tokens de {keycloak}"));
var tokens = new Dictionary<string, string>();
async Task<string> TokenAsync(string usuario)
{
    if (tokens.TryGetValue(usuario, out string? guardado))
    {
        return guardado;
    }
    using var http = new HttpClient();
    var respuesta = await http.PostAsync($"{keycloak}/realms/plataforma/protocol/openid-connect/token", new FormUrlEncodedContent(
        new Dictionary<string, string> { ["grant_type"] = "password", ["client_id"] = "movil", ["username"] = usuario, ["password"] = usuario + "_local" }));
    respuesta.EnsureSuccessStatusCode();
    using var json = System.Text.Json.JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
    return tokens[usuario] = json.RootElement.GetProperty("access_token").GetString()!;
}
if (keycloak is not null)
{
    await TokenAsync("evaluador.armenia");
    await TokenAsync("operador.danos");
}

var simulador = new Simulador(encabezados =>
{
    var cliente = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
    foreach (var (nombre, valor) in encabezados)
    {
        cliente.DefaultRequestHeaders.Add(nombre, valor);
    }
    if (keycloak is not null)
    {
        string usuario = encabezados.TryGetValue("X-Roles", out string? roles) && roles.Contains("operador") ? "operador.danos" : "evaluador.armenia";
        cliente.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens[usuario]);
    }
    return cliente;
}, Console.Out);

var informe = await simulador.EjecutarAsync();
return informe.Pasa ? 0 : 1;
