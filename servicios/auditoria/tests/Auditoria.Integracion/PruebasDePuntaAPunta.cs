using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auditoria.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Pruebas.Contrato;
using Xunit.Abstractions;

namespace Auditoria.Integracion;

/// <summary>
/// El servicio completo contra el bus real: productores con su bandeja y su relevo, la cola q.auditoria con la
/// topología de despliegue/bus y la API. Las pruebas de la colección corren en serie y cada una arranca su propio
/// servicio, para poder dejar eventos en la cola antes de que Auditoría exista.
/// </summary>
[Collection(ColeccionAuditoria.Nombre)]
public class PruebasDePuntaAPunta
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly EntornoAuditoria _entorno;
    private readonly ITestOutputHelper _salida;

    public PruebasDePuntaAPunta(EntornoAuditoria entorno, ITestOutputHelper salida)
    {
        _entorno = entorno;
        _salida = salida;
    }

    private static string ProductorDe(string tipo)
    {
        string productor = CatalogoEventos.PorTipo(tipo).Productor;
        return productor == "adaptador-*" ? "adaptador-se7" : productor;
    }

    [Fact]
    public async Task Todos_los_tipos_en_alcance_quedan_registrados_una_vez_aunque_se_reentreguen_y_aunque_esperen_en_la_cola()
    {
        // 1. Los productores publican un ejemplo de cada tipo en alcance por su bandeja, con Auditoría apagada: los
        //    eventos esperan en q.auditoria, como el atraso que ya acumula Reconstrucción.
        string corrida = Guid.NewGuid().ToString("N")[..8];
        Dictionary<Guid, string> eventos = new();
        foreach (DefinicionEvento definicion in CatalogoEventos.EnAlcance)
        {
            Sobre sobre = EjemplosContrato.Sobre(definicion.Tipo, $"e2e:{corrida}:{definicion.Tipo}");
            await _entorno[ProductorDe(definicion.Tipo)].EscribirAsync(sobre);
            eventos[sobre.IdEvento] = definicion.Tipo;
        }
        foreach (string productor in EntornoAuditoria.Productores)
        {
            await _entorno[productor].PublicarTodoAsync();
        }

        // 2. Reentrega: los relevos vuelven a publicar todo (como tras una conmutación, R-09), y además alguien publica
        //    un mismo evento con otra clave de idempotencia.
        Sobre? repetido = null;
        foreach (string productor in EntornoAuditoria.Productores)
        {
            await using ContextoProductor db = _entorno[productor].Contexto();
            if (repetido is null)
            {
                FilaBandeja fila = await db.Set<FilaBandeja>().FirstAsync(f => f.ClaveIdempotencia.StartsWith($"e2e:{corrida}:"));
                repetido = Sobre.Leer(fila.Sobre) with { SecuenciaProductor = fila.SecuenciaProductor, ClaveIdempotencia = $"otra:{corrida}" };
            }
            await db.Database.ExecuteSqlRawAsync("UPDATE bandeja_salida SET publicado_en = NULL");
            await _entorno[productor].PublicarTodoAsync();
        }
        await _entorno.PublicarCrudoAsync(CatalogoEventos.PorTipo(repetido!.Tipo).ClaveRuteo, repetido.Serializar());
        await EntornoAuditoria.EsperarAsync(async () => await _entorno.MensajesEnColaAsync("q.auditoria") >= 2 * eventos.Count + 1,
            "los eventos y sus repeticiones esperan en q.auditoria");

        // 3. Arranca Auditoría y procesa el atraso.
        await using WebApplicationFactory<Program> servicio = _entorno.Servicio();
        using HttpClient cliente = EntornoAuditoria.Cliente(servicio);
        Stopwatch reloj = Stopwatch.StartNew();
        await EntornoAuditoria.EsperarAsync(async () =>
        {
            await using AuditoriaDbContext db = _entorno.ComoApp();
            bool todos = await db.Registro.CountAsync(r => eventos.Keys.Contains(r.IdEvento)) == eventos.Count;
            bool otraClave = await db.Set<FilaMensajeProcesado>().AnyAsync(m => m.ClaveIdempotencia == $"otra:{corrida}");
            return todos && otraClave;
        }, "Auditoría registra todo el atraso");
        _salida.WriteLine($"Atraso de {2 * eventos.Count + 1} mensajes procesado en {reloj.ElapsedMilliseconds} ms.");
        await EntornoAuditoria.EsperarAsync(async () => await _entorno.MensajesEnColaAsync("q.auditoria") == 0,
            "q.auditoria queda vacía");

        // 4. Una sola entrada por evento, y ninguno fue a la cola de mensajes muertos.
        await using (AuditoriaDbContext db = _entorno.ComoApp())
        {
            List<Guid> registrados = await db.Registro.Where(r => eventos.Keys.Contains(r.IdEvento)).Select(r => r.IdEvento).ToListAsync();
            Assert.Equal(eventos.Count, registrados.Count);
            Assert.Equal(eventos.Keys.Order(), registrados.Order());
            Assert.All(await db.Registro.Where(r => eventos.Keys.Contains(r.IdEvento)).ToListAsync(), r =>
            {
                Assert.NotNull(r.SecuenciaProductor);
                Assert.Equal("Exitoso", r.Resultado);
                Assert.True(r.InstanteRegistro >= r.InstanteEmision);
            });
        }
        Assert.Equal(0, await _entorno.MensajesEnColaAsync("dlq.auditoria"));

        // 5. La API devuelve cada tipo con su interpretación, y la secuencia de cada productor no tiene huecos.
        JsonElement pagina = await cliente.GetFromJsonAsync<JsonElement>("/v1/auditoria?tipo=EV-19&tamano=1000");
        JsonElement ev19 = pagina.GetProperty("entradas").EnumerateArray()
            .Single(e => eventos.ContainsKey(e.GetProperty("idEvento").GetGuid()));
        Assert.Equal("alcaldia.armenia", ev19.GetProperty("actor").GetString());
        Assert.Equal("850000000", ev19.GetProperty("valorNuevo").GetString());
        Assert.Equal("EV-19", ev19.GetProperty("sobre").GetProperty("tipo").GetString());

        await EntornoAuditoria.EsperarAsync(async () =>
        {
            JsonElement huecos = await cliente.GetFromJsonAsync<JsonElement>("/v1/auditoria/huecos");
            List<JsonElement> productores = huecos.GetProperty("productores").EnumerateArray()
                .Where(p => EntornoAuditoria.Productores.Contains(p.GetProperty("productor").GetString())).ToList();
            return productores.Count == EntornoAuditoria.Productores.Length && productores.All(p =>
                p.GetProperty("mayorContiguo").GetInt64() == p.GetProperty("mayorRecibido").GetInt64()
                && p.GetProperty("enPlazo").GetArrayLength() == 0 && p.GetProperty("faltantes").GetArrayLength() == 0);
        }, "cada productor queda revisado y sin huecos");
    }

    [Fact]
    public async Task QA15_un_proyecto_con_N_cambios_tiene_N_entradas_y_cada_una_se_consulta_en_5_s_o_menos()
    {
        await using WebApplicationFactory<Program> servicio = _entorno.Servicio();
        using HttpClient cliente = EntornoAuditoria.Cliente(servicio, "alcaldia.armenia", "autoridad");
        Productor reconstruccion = _entorno["reconstruccion"];
        using CancellationTokenSource fin = new();
        await reconstruccion.Relevo.StartAsync(fin.Token);
        try
        {
            Guid idProyecto = Guid.NewGuid();
            List<Sobre> cambios = CambiosDeUnProyecto(idProyecto);
            Dictionary<Guid, DateTimeOffset> vistos = new();
            string ruta = $"/v1/auditoria?entidad=proyecto&idEntidad={idProyecto}&tamano=1000";

            // Cada cambio es una operación del usuario: un cambio por transacción, con su emisión en ese momento.
            foreach (Sobre plantilla in cambios)
            {
                Sobre sobre = plantilla with { InstanteEmision = DateTimeOffset.UtcNow };
                await reconstruccion.EscribirAsync(sobre);
                await RegistrarVistosAsync(cliente, ruta, vistos);
                await Task.Delay(150);
            }
            await EntornoAuditoria.EsperarAsync(async () =>
            {
                await RegistrarVistosAsync(cliente, ruta, vistos);
                return vistos.Count >= cambios.Count;
            }, "las N entradas están disponibles", segundos: 30);

            JsonElement pagina = await cliente.GetFromJsonAsync<JsonElement>(ruta);
            List<JsonElement> entradas = pagina.GetProperty("entradas").EnumerateArray().ToList();
            // QA-15 (b): N versiones del proyecto, N entradas de auditoría asociadas.
            Assert.Equal(cambios.Count, pagina.GetProperty("total").GetInt64());
            Assert.Equal(cambios.Count, entradas.Count);
            Assert.Equal(cambios.Count, entradas.Select(e => e.GetProperty("idEvento").GetGuid()).Distinct().Count());

            // QA-15 (c): cada entrada está disponible para consulta en 5 s o menos desde su emisión.
            List<double> demoras = entradas.Select(e =>
                (vistos[e.GetProperty("idEvento").GetGuid()] - e.GetProperty("instanteEmision").GetDateTimeOffset()).TotalMilliseconds).ToList();
            _salida.WriteLine($"QA-15 (c), {cambios.Count} cambios: demora máxima {demoras.Max():F0} ms, media {demoras.Average():F0} ms.");
            Assert.All(demoras, d => Assert.InRange(d, 0, 5_000));

            // QA-15 (a): los cambios de presupuesto y de estado llevan valor anterior y nuevo, en orden cronológico.
            Assert.Equal(cambios.Select(c => c.IdEvento), entradas.Select(e => e.GetProperty("idEvento").GetGuid()));
            foreach (JsonElement e in entradas.Where(e => e.GetProperty("accion").GetString() is "CAMBIO_PRESUPUESTO" or "CAMBIO_ESTADO"))
            {
                Assert.False(string.IsNullOrEmpty(e.GetProperty("valorNuevo").GetString()));
                Assert.False(string.IsNullOrEmpty(e.GetProperty("actor").GetString()));
            }
            Assert.Contains(entradas, e => e.GetProperty("accion").GetString() == "CAMBIO_PRESUPUESTO"
                && e.GetProperty("valorAnterior").GetString() == "900000000" && e.GetProperty("valorNuevo").GetString() == "950000000"
                && e.GetProperty("actor").GetString() == "alcaldia.armenia");
            Assert.Contains(entradas, e => e.GetProperty("accion").GetString() == "CAMBIO_ESTADO"
                && e.GetProperty("valorAnterior").GetString() == "FORMULADO" && e.GetProperty("valorNuevo").GetString() == "APROBADO");
        }
        finally
        {
            await fin.CancelAsync();
            await reconstruccion.Relevo.StopAsync(CancellationToken.None);
        }
    }

    private static async Task RegistrarVistosAsync(HttpClient cliente, string ruta, Dictionary<Guid, DateTimeOffset> vistos)
    {
        JsonElement pagina = await cliente.GetFromJsonAsync<JsonElement>(ruta);
        DateTimeOffset ahora = DateTimeOffset.UtcNow;
        foreach (JsonElement e in pagina.GetProperty("entradas").EnumerateArray())
        {
            vistos.TryAdd(e.GetProperty("idEvento").GetGuid(), ahora);
        }
    }

    /// <summary>Doce cambios de un proyecto, con el reparto de tipos de E-10 y versiones consecutivas.</summary>
    private static List<Sobre> CambiosDeUnProyecto(Guid idProyecto)
    {
        ProyectoCreadoV1 creado = (ProyectoCreadoV1)EjemplosContrato.Datos["EV-18"];
        PresupuestoCambiadoV1 presupuesto = (PresupuestoCambiadoV1)EjemplosContrato.Datos["EV-19"];
        EstadoOAvanceCambiadoV1 cambio = (EstadoOAvanceCambiadoV1)EjemplosContrato.Datos["EV-20"];
        ResumenProyecto resumen = creado.Proyecto;
        List<Sobre> sobres = new();
        int version = 0;

        Sobre Evento<T>(DefinicionEvento definicion, T datos) =>
            Sobre.Crear(definicion, "reconstruccion", $"proyecto:{idProyecto}:v{version}", datos, DateTimeOffset.UtcNow);

        ResumenProyecto Resumen(EstadoProyecto estado, decimal? monto, decimal costo, decimal avance) =>
            resumen with { Estado = estado, Presupuesto = monto, CostoEjecutado = costo, Avance = avance, Version = version };

        version++;
        sobres.Add(Evento(CatalogoEventos.ProyectoCreado, creado with { IdProyecto = idProyecto, Proyecto = Resumen(EstadoProyecto.FORMULADO, null, 0, 0) }));
        version++;
        sobres.Add(Evento(CatalogoEventos.PresupuestoCambiado, presupuesto with
        {
            IdProyecto = idProyecto, VersionHistorial = version, PresupuestoAnterior = null, PresupuestoNuevo = 900_000_000m,
            Proyecto = Resumen(EstadoProyecto.FORMULADO, 900_000_000m, 0, 0),
        }));
        version++;
        sobres.Add(Evento(CatalogoEventos.PresupuestoCambiado, presupuesto with
        {
            IdProyecto = idProyecto, TipoCambio = TipoCambioProyecto.FUENTE, VersionHistorial = version,
            PresupuestoAnterior = 900_000_000m, PresupuestoNuevo = 900_000_000m, FuenteAnterior = null, FuenteNueva = "Fondo de reconstrucción",
            Proyecto = Resumen(EstadoProyecto.FORMULADO, 900_000_000m, 0, 0),
        }));
        version++;
        sobres.Add(Evento(CatalogoEventos.PresupuestoCambiado, presupuesto with
        {
            IdProyecto = idProyecto, VersionHistorial = version, PresupuestoAnterior = 900_000_000m, PresupuestoNuevo = 950_000_000m,
            Justificacion = "Ajuste por estudio de suelos", Proyecto = Resumen(EstadoProyecto.FORMULADO, 950_000_000m, 0, 0),
        }));

        EstadoOAvanceCambiadoV1 Cambio(TipoCambioProyecto tipo, EstadoProyecto antes, EstadoProyecto despues, string autor,
            decimal avance, decimal costo, decimal? registrado, string? anterior, string? nuevo) => cambio with
        {
            IdProyecto = idProyecto, TipoCambio = tipo, EstadoAnterior = antes, EstadoNuevo = despues, Autor = autor,
            Avance = avance, CostoEjecutado = costo, CostoRegistrado = registrado, ValorAnterior = anterior, ValorNuevo = nuevo,
            VersionHistorial = version, Proyecto = Resumen(despues, 950_000_000m, costo, avance),
        };

        (TipoCambioProyecto, EstadoProyecto, EstadoProyecto, string, decimal, decimal, decimal?, string?, string?)[] pasos =
        [
            (TipoCambioProyecto.RESPONSABLE, EstadoProyecto.FORMULADO, EstadoProyecto.FORMULADO, "alcaldia.armenia", 0, 0, null, null, "ing.perez"),
            (TipoCambioProyecto.ESTADO, EstadoProyecto.FORMULADO, EstadoProyecto.APROBADO, "alcaldia.armenia", 0, 0, null, "FORMULADO", "APROBADO"),
            (TipoCambioProyecto.ESTADO, EstadoProyecto.APROBADO, EstadoProyecto.EN_EJECUCION, "alcaldia.armenia", 0, 0, null, "APROBADO", "EN_EJECUCION"),
            (TipoCambioProyecto.CONTRATISTA, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION, "ing.perez", 0, 0, null, null, "Constructora Andina"),
            (TipoCambioProyecto.AVANCE, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION, "ing.perez", 20, 0, null, "0", "20"),
            (TipoCambioProyecto.COSTO, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION, "ing.perez", 20, 150_000_000m, 150_000_000m, "0", "150000000"),
            (TipoCambioProyecto.AVANCE, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION, "ing.perez", 45, 150_000_000m, null, "20", "45"),
            (TipoCambioProyecto.ESTADO, EstadoProyecto.EN_EJECUCION, EstadoProyecto.FINALIZADO, "ing.perez", 100, 150_000_000m, null, "EN_EJECUCION", "FINALIZADO"),
        ];
        foreach (var (tipo, antes, despues, autor, avance, costo, registrado, anterior, nuevo) in pasos)
        {
            version++;
            sobres.Add(Evento(CatalogoEventos.EstadoOAvanceCambiado, Cambio(tipo, antes, despues, autor, avance, costo, registrado, anterior, nuevo)));
        }
        return sobres;
    }

    [Fact]
    public async Task R02_con_1_2_4_y_el_plazo_vencido_se_reporta_el_3_y_si_el_3_llega_a_tiempo_no()
    {
        const int plazo = 3;
        await using WebApplicationFactory<Program> servicio = _entorno.Servicio(plazoHuecoSegundos: plazo, intervaloRevisionMs: 300);
        using HttpClient cliente = EntornoAuditoria.Cliente(servicio);
        string conHueco = "productor-a-" + Guid.NewGuid().ToString("N")[..6];
        string aTiempo = "productor-b-" + Guid.NewGuid().ToString("N")[..6];

        // Publicaciones crudas con la secuencia explícita: así se provoca el hueco que un relevo real nunca deja.
        async Task PublicarAsync(string productor, long secuencia)
        {
            Sobre sobre = EjemplosContrato.Sobre("EV-18", $"{productor}:v{secuencia}") with
            {
                Productor = productor, SecuenciaProductor = secuencia, InstanteEmision = DateTimeOffset.UtcNow,
            };
            await _entorno.PublicarCrudoAsync(CatalogoEventos.ProyectoCreado.ClaveRuteo, sobre.Serializar());
        }
        foreach (long s in new long[] { 1, 2, 4 })
        {
            await PublicarAsync(conHueco, s);
            await PublicarAsync(aTiempo, s);
        }
        await Task.Delay(TimeSpan.FromSeconds(1));
        await PublicarAsync(aTiempo, 3);

        JsonElement? reportado = null;
        await EntornoAuditoria.EsperarAsync(async () =>
        {
            JsonElement huecos = await cliente.GetFromJsonAsync<JsonElement>("/v1/auditoria/huecos");
            reportado = huecos.GetProperty("reportados").EnumerateArray()
                .Cast<JsonElement?>().FirstOrDefault(h => h!.Value.GetProperty("productor").GetString() == conHueco);
            return reportado is not null;
        }, "se reporta el hueco 3 del productor A", segundos: 30);

        JsonElement hueco = reportado!.Value;
        Assert.Equal(3, hueco.GetProperty("secuencia").GetInt64());
        Assert.Equal("ABIERTO", hueco.GetProperty("estado").GetString());
        TimeSpan persistio = hueco.GetProperty("reportadoEn").GetDateTimeOffset() - hueco.GetProperty("detectadoEn").GetDateTimeOffset();
        Assert.True(persistio > TimeSpan.FromSeconds(plazo), $"se reportó a los {persistio.TotalSeconds} s, antes del plazo");

        // Ya pasó más del plazo desde que el productor B tuvo su hueco: no se reportó porque el 3 llegó antes.
        await Task.Delay(TimeSpan.FromSeconds(plazo));
        JsonElement final = await cliente.GetFromJsonAsync<JsonElement>("/v1/auditoria/huecos");
        Assert.DoesNotContain(final.GetProperty("reportados").EnumerateArray(), h => h.GetProperty("productor").GetString() == aTiempo);
        JsonElement b = final.GetProperty("productores").EnumerateArray().Single(p => p.GetProperty("productor").GetString() == aTiempo);
        Assert.Equal(4, b.GetProperty("mayorContiguo").GetInt64());
        JsonElement a = final.GetProperty("productores").EnumerateArray().Single(p => p.GetProperty("productor").GetString() == conHueco);
        Assert.Equal(2, a.GetProperty("mayorContiguo").GetInt64());
        Assert.Equal(4, a.GetProperty("mayorRecibido").GetInt64());
        Assert.Equal([3L], a.GetProperty("faltantes").EnumerateArray().Select(x => x.GetInt64()));
    }

    [Fact]
    public async Task La_API_es_de_solo_lectura_y_solo_para_auditor_y_autoridad()
    {
        await using WebApplicationFactory<Program> servicio = _entorno.Servicio();

        // Ninguna ruta del servicio acepta otro método que GET (AD-21: no existe ruta de escritura).
        IEnumerable<RouteEndpoint> rutas = servicio.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        List<RouteEndpoint> deApi = rutas.Where(r => r.RoutePattern.RawText!.StartsWith("v1/", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, deApi.Count);
        Assert.All(deApi, r => Assert.Equal(["GET"], r.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));

        using HttpClient anonimo = servicio.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/v1/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/v1/auditoria/huecos")).StatusCode);

        using HttpClient evaluador = EntornoAuditoria.Cliente(servicio, "ana.perez", "evaluador");
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluador.GetAsync("/v1/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluador.GetAsync("/v1/auditoria/huecos")).StatusCode);

        using HttpClient auditor = EntornoAuditoria.Cliente(servicio, "contraloria", "auditor");
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/v1/auditoria?pagina=1&tamano=10")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await auditor.GetAsync("/v1/auditoria?pagina=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await auditor.GetAsync("/v1/auditoria?desde=2026-12-01T00:00:00Z&hasta=2026-11-01T00:00:00Z")).StatusCode);
        foreach (HttpMethod metodo in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            HttpResponseMessage respuesta = await auditor.SendAsync(new HttpRequestMessage(metodo, "/v1/auditoria")
            {
                Content = JsonContent.Create(new { tipo = "EV-18" }),
            });
            Assert.Contains(respuesta.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
        }

        using HttpClient autoridad = EntornoAuditoria.Cliente(servicio, "gobernacion.quindio", "autoridad");
        Assert.Equal(HttpStatusCode.OK, (await autoridad.GetAsync("/v1/auditoria/huecos")).StatusCode);

        // Listo: la base y la cola del bus responden con el usuario de Auditoría (mínimo privilegio).
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/salud/listo")).StatusCode);
    }
}
