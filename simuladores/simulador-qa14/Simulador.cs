using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SimuladorQa14;

public sealed record CriterioQa14(string Criterio, string Esperado, string Obtenido, bool Pasa);

/// <summary>Resultado de una corrida: los criterios de QA-14 y los tres números que lo resumen.</summary>
public sealed record InformeQa14(IReadOnlyList<CriterioQa14> Criterios, int Perdidas, int SobrescriturasSilenciosas,
    int Duplicadas, TimeSpan Duracion)
{
    public bool Pasa => Criterios.All(c => c.Pasa);
}

/// <summary>
/// Escenario de QA-14 (ATAM, hoja A,A): 120 evaluaciones registradas sin conexión por un dispositivo, 5 de ellas sobre
/// activos que otro dispositivo cambió después de la descarga; el lote se envía dos veces. Se espera 0 pérdidas,
/// 0 sobrescrituras silenciosas (los 5 conflictos se detectan y conservan ambas versiones) y 0 duplicados.
/// Es el del prototipo. Cambios: recibe la fábrica de clientes HTTP (para correr contra varias réplicas), consulta la
/// bandeja de revisión con el rol de operador (la ruta ahora lo exige) y cuenta de forma explícita las sobrescrituras.
/// </summary>
/// <param name="clientes">Crea un cliente HTTP con los encabezados de identidad que recibe.</param>
public sealed class Simulador(Func<IReadOnlyDictionary<string, string>, HttpClient> clientes, TextWriter salida)
{
    public const int Total = 120;
    public const int EnConflicto = 5;
    public const int TamanoLote = 20;
    private static readonly TimeSpan Limite = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<InformeQa14> EjecutarAsync()
    {
        string corrida = Guid.NewGuid().ToString("N")[..8];
        string dispositivoA = $"sim-A-{corrida}";
        string dispositivoB = $"sim-B-{corrida}";
        var reloj = Stopwatch.StartNew();
        salida.WriteLine($"Corrida {corrida}");

        using var http = Evaluador(dispositivoA);
        var catalogo = await http.GetFromJsonAsync<Descarga>("/v1/descarga", Json)
            ?? throw new InvalidOperationException("La descarga vino vacía");
        if (catalogo.TiposActivo.Count == 0)
        {
            throw new InvalidOperationException("El servicio no tiene catálogo de tipos de activo (migre con --sembrar)");
        }
        // Los desastres llegan por eventos de Gestión de Desastres. Si aún no hay ninguno, las evaluaciones se
        // registran como preliminares (AD-29): la sincronización y sus garantías son las mismas.
        Guid? desastreId = catalogo.Desastres.Count > 0 ? catalogo.Desastres[0].Id : null;
        if (desastreId is null)
        {
            salida.WriteLine("   Sin desastres activos: las evaluaciones se registran como preliminares.");
        }

        // 0. Preparación: un dispositivo aparte crea en campo los 120 activos que se van a evaluar (el servicio no
        //    siembra activos). Cuadrícula de 12 × 10 con ~220 m entre puntos y un corrimiento al azar por corrida.
        string dispositivoP = $"sim-P-{corrida}";
        using var httpP = Evaluador(dispositivoP);
        var origenLat = 4.58 + Random.Shared.NextDouble() * 0.002;
        var origenLon = -74.14 + Random.Shared.NextDouble() * 0.002;
        var nuevos = new HashSet<Guid>();
        var opsP = new List<Operacion>();
        for (int i = 0; i < Total; i++)
        {
            var tipo = catalogo.TiposActivo[i % catalogo.TiposActivo.Count].Codigo;
            double lat = Math.Round(origenLat + (i / 12) * 0.002, 6);
            double lon = Math.Round(origenLon + (i % 12) * 0.002, 6);
            var id = Guid.NewGuid();
            nuevos.Add(id);
            var nuevo = new ActivoNuevo(id, tipo, lat, lon, $"Activo simulado {i + 1:000} · corrida {corrida}");
            var evaluacion = new EvaluacionEntrante(Guid.NewGuid(), id, null, DateTime.UtcNow.AddHours(-2), "Preparación",
                "Simulador QA-14", lat, lon, 5.0, tipo, "SIN_DANO", "Registro inicial del activo", [], nuevo);
            opsP.Add(new Operacion(Guid.NewGuid(), "CREAR", null, evaluacion));
        }
        foreach (var lote in opsP.Chunk(TamanoLote))
        {
            await Sincronizar(httpP, dispositivoP, lote.ToList());
        }
        salida.WriteLine($"0. Preparación: {Total} activos nuevos creados en campo por {dispositivoP}.");

        // 1. Descarga previa (la misma para los dos dispositivos, antes de que B cambie nada).
        var descarga = await http.GetFromJsonAsync<Descarga>("/v1/descarga", Json)
            ?? throw new InvalidOperationException("La descarga vino vacía");
        var activos = descarga.Activos.Where(a => nuevos.Contains(a.Id)).OrderBy(a => a.Referencia).ToList();
        if (activos.Count < Total)
        {
            throw new InvalidOperationException($"Se crearon {Total} activos y la descarga solo trae {activos.Count}");
        }
        salida.WriteLine($"1. Descarga: {descarga.Activos.Count} activos, {descarga.TiposActivo.Count} tipos, {descarga.Desastres.Count} desastres.");

        // 2. El dispositivo B evalúa 5 de esos activos primero: sube su versión en el servidor.
        using var httpB = Evaluador(dispositivoB);
        var opsB = new List<Operacion>();
        for (int i = 0; i < EnConflicto; i++)
        {
            opsB.Add(NuevaOperacion(activos[i], desastreId, "Bruno Díaz", "LEVE", foto: null));
        }
        var respuestaB = await Sincronizar(httpB, dispositivoB, opsB);
        int aplicadasB = respuestaB.Resultados.Count(r => r.Resultado == "APLICADA");
        var evaluacionesB = opsB.Select(o => o.Evaluacion.Id).ToHashSet();
        salida.WriteLine($"2. Dispositivo B: {aplicadasB}/{EnConflicto} aplicadas (suben la versión de esos activos).");

        // 3. El dispositivo A envía 120 CREAR con la versión que descargó ANTES del paso 2.
        var fotos = new Dictionary<Guid, byte[]>();
        var operacionesA = new List<Operacion>();
        for (int i = 0; i < Total; i++)
        {
            var foto = FotoJpeg();
            var op = NuevaOperacion(activos[i], desastreId, "Ana Pérez", i % 2 == 0 ? "GRAVE" : "MODERADO", foto);
            fotos[op.Evaluacion.Evidencias[0].Id] = foto;
            operacionesA.Add(op);
        }
        var lotes = operacionesA.Chunk(TamanoLote).Select(c => c.ToList()).ToList();
        var primeras = new List<RespuestaLote>();
        foreach (var lote in lotes)
        {
            primeras.Add(await Sincronizar(http, dispositivoA, lote));
        }
        var resultadosA = primeras.SelectMany(r => r.Resultados).ToList();
        salida.WriteLine($"3. Dispositivo A: {lotes.Count} lotes de {TamanoLote} enviados.");

        int subidas = 0;
        foreach (var (evidenciaId, foto) in fotos)
        {
            using var contenido = new MultipartFormDataContent();
            var parte = new ByteArrayContent(foto);
            parte.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            contenido.Add(parte, "archivo", $"{evidenciaId}.jpg");
            var respuesta = await http.PutAsync($"/v1/evidencias/{evidenciaId}", contenido);
            if (respuesta.IsSuccessStatusCode)
            {
                subidas++;
            }
        }
        salida.WriteLine($"   Fotos subidas: {subidas}/{fotos.Count}.");

        // 4. Reenvío de exactamente los mismos lotes (como si la red se hubiera caído antes de la respuesta).
        var segundas = new List<RespuestaLote>();
        foreach (var lote in lotes)
        {
            segundas.Add(await Sincronizar(http, dispositivoA, lote));
        }
        salida.WriteLine("4. Reenvío de los mismos lotes completado.");

        // 5. Verificación contra el servidor. La bandeja de revisión la lee un operador (AD-32).
        var evaluaciones = await http.GetFromJsonAsync<List<EvaluacionResumen>>($"/v1/evaluaciones?idDispositivo={dispositivoA}", Json) ?? [];
        var evaluacionesDeB = await httpB.GetFromJsonAsync<List<EvaluacionResumen>>($"/v1/evaluaciones?idDispositivo={dispositivoB}", Json) ?? [];
        using var httpOperador = clientes(new Dictionary<string, string>
        {
            ["X-Usuario"] = "simulador.operador", ["X-Roles"] = "operador", ["X-Organizacion"] = "Simulador QA-14",
        });
        var conflictos = await httpOperador.GetFromJsonAsync<List<ConflictoResumen>>("/v1/conflictos?estado=PENDIENTE", Json) ?? [];
        reloj.Stop();

        var idsA = operacionesA.Select(o => o.Evaluacion.Id).ToHashSet();
        var activosB = activos.Take(EnConflicto).Select(a => a.Id).ToHashSet();
        var conflictosA = conflictos.Where(c => idsA.Contains(c.Entrante.EvaluacionId)).ToList();
        int conflictosDetectados = conflictosA.Count(c => activosB.Contains(c.ActivoId) && c.Vigente != null);
        var mismosResultados = JsonSerializer.Serialize(primeras, Json) == JsonSerializer.Serialize(segundas, Json);
        int aplicadas = resultadosA.Count(r => r.Resultado == "APLICADA");
        int enConflicto = resultadosA.Count(r => r.Resultado == "EN_CONFLICTO");
        bool conflictoEnLosDeB = resultadosA.Take(EnConflicto).All(r => r.Resultado == "EN_CONFLICTO");
        int perdidas = idsA.Count(id => evaluaciones.All(e => e.Id != id));
        int duplicadas = evaluaciones.Count - evaluaciones.Select(e => e.Id).Distinct().Count()
                         + evaluaciones.Count(e => e.NumeroVersiones != 1)
                         + (conflictosA.Count - conflictosA.Select(c => c.Entrante.EvaluacionId).Distinct().Count());
        // Sobrescritura silenciosa: una evaluación de B que ya no es la vigente de su activo (o cambió) sin que
        // haya quedado un conflicto con ambas versiones para la revisión humana.
        int sobrescrituras = evaluacionesB.Count(id =>
            !evaluacionesDeB.Any(e => e.Id == id && e.Estado == "REGISTRADA" && e.NivelDano == "LEVE" && e.NumeroVersiones == 1)
            || !conflictosA.Any(c => c.Vigente != null && c.Vigente.EvaluacionId == id && c.Vigente.IdDispositivo == dispositivoB));
        if (desastreId is null)
        {
            // Sin desastre las evaluaciones de B quedan PRELIMINAR, no REGISTRADA.
            sobrescrituras = evaluacionesB.Count(id =>
                !evaluacionesDeB.Any(e => e.Id == id && e.NivelDano == "LEVE" && e.NumeroVersiones == 1)
                || !conflictosA.Any(c => c.Vigente != null && c.Vigente.EvaluacionId == id && c.Vigente.IdDispositivo == dispositivoB));
        }
        int evidenciasSubidas = evaluaciones.Sum(e => e.EvidenciasSubidas);

        var criterios = new List<CriterioQa14>
        {
            new("Evaluaciones APLICADA", $"{Total - EnConflicto}", $"{aplicadas}", aplicadas == Total - EnConflicto),
            new("Evaluaciones EN_CONFLICTO", $"{EnConflicto}", $"{enConflicto}", enConflicto == EnConflicto && conflictoEnLosDeB),
            new("Conflictos detectados (ambas versiones)", $"{EnConflicto} (100 %)", $"{conflictosDetectados}", conflictosDetectados == EnConflicto),
            new("Pérdidas", "0", $"{perdidas}", perdidas == 0 && evaluaciones.Count == Total),
            new("Sobrescrituras silenciosas", "0", $"{sobrescrituras}", sobrescrituras == 0 && aplicadasB == EnConflicto),
            new("Duplicados tras el reenvío", "0", $"{duplicadas}", duplicadas == 0),
            new("Resultados idénticos en el reenvío", "sí", mismosResultados ? "sí" : "no", mismosResultados),
            new("Evidencias subidas", $"{Total}", $"{evidenciasSubidas}", evidenciasSubidas == Total && subidas == Total),
            new("Tiempo total", "≤ 10 min", $"{reloj.Elapsed.TotalSeconds:0.0} s", reloj.Elapsed <= Limite),
        };
        var informe = new InformeQa14(criterios, perdidas, sobrescrituras, duplicadas, reloj.Elapsed);

        salida.WriteLine();
        salida.WriteLine($"{"Criterio QA-14",-42} {"Esperado",-12} {"Obtenido",-12} Resultado");
        salida.WriteLine(new string('-', 80));
        foreach (var c in criterios)
        {
            salida.WriteLine($"{c.Criterio,-42} {c.Esperado,-12} {c.Obtenido,-12} {(c.Pasa ? "PASA" : "FALLA")}");
        }
        salida.WriteLine(new string('-', 80));
        salida.WriteLine(informe.Pasa ? "QA-14: PASA" : "QA-14: FALLA");
        return informe;
    }

    // ---------- Funciones auxiliares ----------

    /// <summary>Como la app actual: usuario, organización y dispositivo, sin roles (se trata como evaluador).</summary>
    private HttpClient Evaluador(string dispositivo) => clientes(new Dictionary<string, string>
    {
        ["X-Usuario"] = "simulador", ["X-Organizacion"] = "Simulador QA-14", ["X-Dispositivo"] = dispositivo,
    });

    private static Operacion NuevaOperacion(Activo activo, Guid? desastreId, string evaluador, string nivel, byte[]? foto)
    {
        var evidencias = new List<EvidenciaDeclarada>();
        if (foto != null)
        {
            evidencias.Add(new EvidenciaDeclarada(Guid.NewGuid(), activo.Lat, activo.Lon, DateTime.UtcNow.AddMinutes(-20),
                Convert.ToHexStringLower(SHA256.HashData(foto)), foto.Length));
        }
        var evaluacion = new EvaluacionEntrante(Guid.NewGuid(), activo.Id, desastreId, DateTime.UtcNow.AddMinutes(-20),
            evaluador, "Cruz Roja", activo.Lat, activo.Lon, 5.0, activo.TipoCodigo, nivel,
            $"Evaluación simulada de {activo.Referencia}", evidencias, null);
        return new Operacion(Guid.NewGuid(), "CREAR", activo.Version, evaluacion);
    }

    private static async Task<RespuestaLote> Sincronizar(HttpClient cliente, string dispositivo, List<Operacion> operaciones)
    {
        var lote = new Lote(dispositivo, DateTime.UtcNow, operaciones);
        var respuesta = await cliente.PostAsJsonAsync("/v1/sincronizacion/evaluaciones", lote, Json);
        var texto = await respuesta.Content.ReadAsStringAsync();
        if (!respuesta.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)respuesta.StatusCode}: {texto}");
        }
        return JsonSerializer.Deserialize<RespuestaLote>(texto, Json)!;
    }

    private static byte[] FotoJpeg()
    {
        var bytes = new byte[32 * 1024];
        Random.Shared.NextBytes(bytes);
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0;
        bytes[^2] = 0xFF; bytes[^1] = 0xD9;
        return bytes;
    }

    // ---------- Contrato (solo lo que el simulador usa) ----------

    private record Descarga(List<TipoActivo> TiposActivo, List<Desastre> Desastres, List<Activo> Activos);
    private record TipoActivo(string Codigo, string Nombre);
    private record Desastre(Guid Id, string Nombre);
    private record Activo(Guid Id, string TipoCodigo, double Lat, double Lon, string Referencia, int Version);
    private record Lote(string IdDispositivo, DateTime InstanteEnvioDispositivo, List<Operacion> Operaciones);
    private record Operacion(Guid IdOperacion, string Tipo, int? VersionBaseActivo, EvaluacionEntrante Evaluacion);
    private record EvaluacionEntrante(Guid Id, Guid ActivoId, Guid? DesastreId, DateTime InstanteCampo, string Evaluador,
        string Organizacion, double Lat, double Lon, double PrecisionM, string TipoActivoCodigo, string NivelDano,
        string Descripcion, List<EvidenciaDeclarada> Evidencias, ActivoNuevo? ActivoNuevo);
    private record ActivoNuevo(Guid Id, string TipoCodigo, double Lat, double Lon, string Referencia);
    private record EvidenciaDeclarada(Guid Id, double Lat, double Lon, DateTime InstanteCaptura, string Sha256, long TamanoBytes);
    private record RespuestaLote(List<ResultadoOperacion> Resultados);
    private record ResultadoOperacion(Guid IdOperacion, string Resultado, string? EstadoEvaluacion, string? Motivo,
        int? NuevaVersionActivo, DateTime InstanteServidor);
    private record EvaluacionResumen(Guid Id, string Estado, string NivelDano, int NumeroVersiones, int EvidenciasDeclaradas, int EvidenciasSubidas);
    private record ConflictoResumen(Guid Id, Guid ActivoId, string Tipo, VersionResumen? Vigente, VersionResumen Entrante);
    private record VersionResumen(Guid EvaluacionId, int NumeroVersion, string IdDispositivo);
}
