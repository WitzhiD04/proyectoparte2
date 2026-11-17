using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Xunit.Abstractions;
using static ConsultaDanos.Integracion.Eventos;

namespace ConsultaDanos.Integracion;

public class PruebasConsultaDanosIntegracion : IClassFixture<EntornoConsultaDanos>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private readonly EntornoConsultaDanos _e;
    private readonly ITestOutputHelper _salida;

    public PruebasConsultaDanosIntegracion(EntornoConsultaDanos e, ITestOutputHelper salida)
    {
        _e = e;
        _salida = salida;
    }

    private Task<JsonElement> GetAsync(string ruta) => _e.Cliente.GetFromJsonAsync<JsonElement>(ruta);

    private static IEnumerable<JsonElement> Features(JsonElement mapa, string tipo) =>
        mapa.GetProperty("features").EnumerateArray().Where(f => f.GetProperty("properties").GetProperty("tipo").GetString() == tipo);

    private static JsonElement Zona(JsonElement mapa, string codigo) =>
        Features(mapa, "zona").Single(f => f.GetProperty("properties").GetProperty("codigoMunicipio").GetString() == codigo);

    [Fact]
    public async Task Los_eventos_desordenados_y_repetidos_dejan_la_proyeccion_correcta()
    {
        Guid desastre = Guid.NewGuid(), activo = Guid.NewGuid(), evaluacion = Guid.NewGuid();
        Sobre v3 = Sincronizada(activo, evaluacion, desastre, NivelDano.GRAVE, 3, 3, T0.AddMinutes(3));
        // Llega primero la validación de la versión 4, después la evaluación vigente, la vieja y repeticiones.
        await _e.EntregarAsync(Validada(activo, evaluacion, desastre, NivelDano.GRAVE, 4, EstadoValidacion.VALIDADA, T0.AddMinutes(4)));
        await _e.EntregarAsync(v3);
        await _e.EntregarAsync(Sincronizada(activo, evaluacion, desastre, NivelDano.LEVE, 1, 1, T0));
        await _e.EntregarAsync(v3 with { IdEvento = Guid.NewGuid() });   // la misma versión otra vez, con otro sobre
        await _e.EntregarAsync(Validada(activo, evaluacion, desastre, NivelDano.GRAVE, 4, EstadoValidacion.VALIDADA, T0.AddMinutes(4)));

        JsonElement evolucion = await GetAsync($"/v1/consulta-danos/activos/{activo}/evolucion");
        JsonElement vigente = evolucion.GetProperty("vigente");
        Assert.Equal("GRAVE", vigente.GetProperty("nivelDano").GetString());
        Assert.Equal(3, vigente.GetProperty("versionActivo").GetInt32());
        Assert.True(vigente.GetProperty("validada").GetBoolean());
        // RF-ED-15: la secuencia de evaluaciones en el tiempo, sin duplicados, ordenada por instante.
        Assert.Equal(["LEVE", "GRAVE", "GRAVE"], evolucion.GetProperty("evaluaciones").EnumerateArray().Select(x => x.GetProperty("nivelDano").GetString()));
        Assert.Equal(T0.AddMinutes(4), evolucion.GetProperty("datoAl").GetDateTimeOffset());

        JsonElement consolidado = await GetAsync($"/v1/consulta-danos/consolidado?desastre={desastre}");
        Assert.Equal(1, consolidado.GetProperty("total").GetProperty("activos").GetInt32());
        Assert.Equal(1, consolidado.GetProperty("total").GetProperty("distribucion").GetProperty("GRAVE").GetInt32());
        Assert.Equal(T0.AddMinutes(4), consolidado.GetProperty("datoAl").GetDateTimeOffset());
    }

    [Fact]
    public async Task Una_evaluacion_entra_al_consolidado_cuando_EV08_la_valida_y_sale_si_vuelve_a_pendiente()
    {
        Guid desastre = Guid.NewGuid(), activo = Guid.NewGuid(), evaluacion = Guid.NewGuid();
        string ruta = $"/v1/consulta-danos/consolidado?desastre={desastre}";
        await _e.EntregarAsync(Sincronizada(activo, evaluacion, desastre, NivelDano.MODERADO, 1, 1, T0));
        Assert.Equal(0, (await GetAsync(ruta)).GetProperty("total").GetProperty("activos").GetInt32());

        await _e.EntregarAsync(Validada(activo, evaluacion, desastre, NivelDano.MODERADO, 2, EstadoValidacion.VALIDADA, T0.AddMinutes(1)));
        JsonElement validada = await GetAsync(ruta);
        Assert.Equal(1, validada.GetProperty("total").GetProperty("activos").GetInt32());
        JsonElement zona = validada.GetProperty("zonas")[0];
        Assert.Equal("63001", zona.GetProperty("codigoMunicipio").GetString());
        Assert.Equal("Armenia", zona.GetProperty("nombre").GetString());
        Assert.Equal(0.5, zona.GetProperty("indiceDano").GetDouble());

        await _e.EntregarAsync(Validada(activo, evaluacion, desastre, NivelDano.MODERADO, 3, EstadoValidacion.PENDIENTE, T0.AddMinutes(2)));
        JsonElement pendiente = await GetAsync(ruta);
        Assert.Equal(0, pendiente.GetProperty("total").GetProperty("activos").GetInt32());
        Assert.Empty(pendiente.GetProperty("zonas").EnumerateArray());
    }

    [Fact]
    public async Task El_consolidado_prioriza_las_zonas_con_activos_criticos_graves_RFED10()
    {
        Guid desastre = Guid.NewGuid();
        async Task ValidadaAsync(string municipio, string tipo, NivelDano nivel)
        {
            Guid a = Guid.NewGuid(), ev = Guid.NewGuid();
            await _e.EntregarAsync(Sincronizada(a, ev, desastre, nivel, 1, 1, T0, municipio, tipo));
            await _e.EntregarAsync(Validada(a, ev, desastre, nivel, 2, EstadoValidacion.VALIDADA, T0.AddMinutes(1), municipio, tipo));
        }
        await ValidadaAsync("63001", "VIVIENDA", NivelDano.DESTRUCCION_TOTAL);
        await ValidadaAsync("63001", "VIVIENDA", NivelDano.DESTRUCCION_TOTAL);
        await ValidadaAsync("63130", "HOSPITAL", NivelDano.GRAVE);
        await _e.EntregarAsync(Sobre.Crear(CatalogoEventos.TipoActivoActualizado, CatalogoEventos.EvaluacionDanos, "tipo-activo:HOSPITAL:v1",
            new TipoActivoActualizadoV1("HOSPITAL", "Hospital", Enum.GetValues<NivelDano>().Select(n => new CriterioNivel(n, "criterio")).ToList(), 1),
            T0));

        JsonElement consolidado = await GetAsync($"/v1/consulta-danos/consolidado?desastre={desastre}");
        JsonElement[] zonas = consolidado.GetProperty("zonas").EnumerateArray().ToArray();
        Assert.Equal("63130", zonas[0].GetProperty("codigoMunicipio").GetString());   // un hospital grave pesa más que dos viviendas destruidas
        Assert.Equal(1, zonas[0].GetProperty("prioridad").GetInt32());
        Assert.Equal(1, zonas[0].GetProperty("criticosGraves").GetInt32());
        Assert.Equal("Hospital", zonas[0].GetProperty("porTipo")[0].GetProperty("nombre").GetString());
        Assert.Equal(1.0, zonas[1].GetProperty("indiceDano").GetDouble());
        Assert.Equal(3, consolidado.GetProperty("total").GetProperty("activos").GetInt32());
    }

    [Fact]
    public async Task El_mapa_trae_la_coropleta_y_los_puntos_y_un_cambio_lo_invalida()
    {
        Guid desastre = Guid.NewGuid();
        Guid a1 = Guid.NewGuid(), a2 = Guid.NewGuid(), a3 = Guid.NewGuid(), e3 = Guid.NewGuid();
        await _e.EntregarAsync(Sincronizada(a1, Guid.NewGuid(), desastre, NivelDano.GRAVE, 1, 1, T0));
        await _e.EntregarAsync(Sincronizada(a2, Guid.NewGuid(), desastre, NivelDano.LEVE, 1, 1, T0.AddMinutes(1)));
        await _e.EntregarAsync(Sincronizada(a3, e3, desastre, NivelDano.DESTRUCCION_TOTAL, 1, 1, T0.AddMinutes(2), "63130", lon: -75.60));
        await _e.EntregarAsync(Validada(a3, e3, desastre, NivelDano.DESTRUCCION_TOTAL, 2, EstadoValidacion.VALIDADA, T0.AddMinutes(3), "63130"));

        string ruta = $"/v1/consulta-danos/mapa?desastre={desastre}&puntos=true";
        HttpResponseMessage respuesta = await _e.Cliente.GetAsync(ruta);
        Assert.Equal("application/geo+json", respuesta.Content.Headers.ContentType!.MediaType);
        JsonElement mapa = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FeatureCollection", mapa.GetProperty("type").GetString());
        Assert.False(mapa.GetProperty("degradado").GetBoolean());
        Assert.Equal(T0.AddMinutes(3), mapa.GetProperty("datoAl").GetDateTimeOffset());

        // Coropleta: la geometría simplificada del municipio con el índice y la distribución por nivel (RF-ED-11).
        JsonElement armenia = Zona(mapa, "63001");
        Assert.Equal("Polygon", armenia.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(2, armenia.GetProperty("properties").GetProperty("activos").GetInt32());
        Assert.Equal(0.5, armenia.GetProperty("properties").GetProperty("indiceDano").GetDouble());   // (0,75 + 0,25) / 2
        Assert.Equal(1, armenia.GetProperty("properties").GetProperty("distribucion").GetProperty("LEVE").GetInt32());

        // Puntos: nivel, si está validada y si tiene conflicto pendiente (AD-32).
        JsonElement[] puntos = Features(mapa, "activo").ToArray();
        Assert.Equal(3, puntos.Length);
        JsonElement p3 = puntos.Single(p => p.GetProperty("properties").GetProperty("idActivo").GetGuid() == a3);
        Assert.True(p3.GetProperty("properties").GetProperty("validada").GetBoolean());
        Assert.False(p3.GetProperty("properties").GetProperty("conflictoPendiente").GetBoolean());
        Assert.Equal(-75.60, p3.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble());

        // La caché guarda la geometría por capa y versión, y el mapa armado por versión del desastre (DA-08).
        Assert.True(await _e.ExisteEnCacheAsync("consulta-danos:geo:MUNICIPIOS:1:63001"));
        Assert.True(await _e.ExisteEnCacheAsync($"consulta-danos:mapa:{desastre}:g*"));

        // Filtros: solo validadas deja solo Calarcá; nivel mínimo GRAVE saca la leve.
        JsonElement validadas = await GetAsync($"/v1/consulta-danos/mapa?desastre={desastre}&soloValidadas=true");
        Assert.Equal(["63130"], Features(validadas, "zona").Select(f => f.GetProperty("properties").GetProperty("codigoMunicipio").GetString()));
        Assert.Empty(Features(validadas, "activo"));
        JsonElement graves = await GetAsync($"/v1/consulta-danos/mapa?desastre={desastre}&nivelMinimo=grave");
        Assert.Equal(1, Zona(graves, "63001").GetProperty("properties").GetProperty("activos").GetInt32());

        // Un evento de ese desastre invalida el mapa armado: la consulta siguiente ya lo refleja.
        await _e.EntregarAsync(Sincronizada(Guid.NewGuid(), Guid.NewGuid(), desastre, NivelDano.SIN_DANO, 1, 1, T0.AddMinutes(5)));
        JsonElement despues = await GetAsync(ruta);
        Assert.Equal(3, Zona(despues, "63001").GetProperty("properties").GetProperty("activos").GetInt32());
        Assert.Equal(T0.AddMinutes(5), despues.GetProperty("datoAl").GetDateTimeOffset());
    }

    [Fact]
    public async Task Un_conflicto_marca_el_punto_y_entra_en_los_indicadores_hasta_que_se_resuelve_R08()
    {
        Guid desastre = Guid.NewGuid(), activo = Guid.NewGuid(), vigente = Guid.NewGuid(), entrante = Guid.NewGuid(), conflicto = Guid.NewGuid();
        await _e.EntregarAsync(Sincronizada(activo, vigente, desastre, NivelDano.LEVE, 1, 1, T0));
        int antes = (await GetAsync("/v1/consulta-danos/indicadores-revision")).GetProperty("conflictosPendientes").GetInt32();
        string ruta = $"/v1/consulta-danos/mapa?desastre={desastre}&puntos=true";
        await GetAsync(ruta);   // queda en la caché

        // La entrante no se aplicó: EV-09 trae su contenido con la versión del activo que ya estaba y no reemplaza la vigente.
        await _e.EntregarAsync(Sincronizada(activo, entrante, desastre, NivelDano.DESTRUCCION_TOTAL, 1, 1, T0.AddMinutes(1), enConflicto: true));
        await _e.EntregarAsync(ConflictoDetectado(conflicto, activo, vigente, entrante, T0.AddDays(-2)));

        JsonElement indicadores = await GetAsync("/v1/consulta-danos/indicadores-revision");
        Assert.Equal(antes + 1, indicadores.GetProperty("conflictosPendientes").GetInt32());
        Assert.Equal(50, indicadores.GetProperty("umbralConflictos").GetInt32());
        Assert.Equal(24, indicadores.GetProperty("umbralHoras").GetDouble());
        Assert.True(indicadores.GetProperty("antiguedadHoras").GetDouble() > 0);
        Assert.True(indicadores.GetProperty("superaAntiguedad").GetBoolean());   // el conflicto se detectó hace más de dos días
        Assert.True(indicadores.GetProperty("alarma").GetBoolean());

        JsonElement punto = Features(await GetAsync(ruta), "activo").Single();
        Assert.True(punto.GetProperty("properties").GetProperty("conflictoPendiente").GetBoolean());
        Assert.Equal("LEVE", punto.GetProperty("properties").GetProperty("nivelDano").GetString());

        await _e.EntregarAsync(ConflictoResuelto(conflicto, activo, vigente, desastre, NivelDano.LEVE, 2, 1, T0.AddMinutes(2)));
        Assert.Equal(antes, (await GetAsync("/v1/consulta-danos/indicadores-revision")).GetProperty("conflictosPendientes").GetInt32());
        Assert.False(Features(await GetAsync(ruta), "activo").Single().GetProperty("properties").GetProperty("conflictoPendiente").GetBoolean());
    }

    [Fact]
    public async Task Eventos_aplicados_a_la_vez_como_dos_replicas_no_pierden_cuentas_del_consolidado()
    {
        Guid desastre = Guid.NewGuid();
        List<Sobre> eventos = [];
        for (int i = 0; i < 30; i++)
        {
            Guid a = Guid.NewGuid(), ev = Guid.NewGuid();
            eventos.Add(Sincronizada(a, ev, desastre, NivelDano.GRAVE, 1, 1, T0));
            eventos.Add(Validada(a, ev, desastre, NivelDano.GRAVE, 2, EstadoValidacion.VALIDADA, T0.AddMinutes(1)));
        }
        // Todos a la vez y en desorden: el mismo grupo del consolidado y, por activo, la sincronización y su validación.
        await Task.WhenAll(eventos.OrderBy(_ => Random.Shared.Next()).Select(s => Task.Run(() => _e.EntregarAsync(s))));

        JsonElement consolidado = await GetAsync($"/v1/consulta-danos/consolidado?desastre={desastre}");
        Assert.Equal(30, consolidado.GetProperty("total").GetProperty("distribucion").GetProperty("GRAVE").GetInt32());
        JsonElement mapa = await GetAsync($"/v1/consulta-danos/mapa?desastre={desastre}&soloValidadas=true");
        Assert.Equal(30, Zona(mapa, "63001").GetProperty("properties").GetProperty("activos").GetInt32());
    }

    /// <summary>
    /// Referencia para QA-06 (criterio de salida de F-4): el mapa de un desastre con 5 000 activos, con los puntos, se arma
    /// desde PostGIS en menos de 2 s, y desde la caché en mucho menos. No reemplaza la prueba de carga (k6): mide una
    /// petición sola, sin concurrencia. Mide tiempo absoluto: en una máquina lenta se excluye con
    /// <c>--filter "Categoria!=Rendimiento"</c>.
    /// </summary>
    [Fact]
    [Trait("Categoria", "Rendimiento")]
    public async Task El_mapa_de_5000_activos_se_arma_en_menos_de_2_s_QA06()
    {
        Guid desastre = Guid.NewGuid();
        await Parallel.ForEachAsync(Enumerable.Range(0, 5000), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, _) =>
            await _e.EntregarAsync(Sincronizada(Guid.NewGuid(), Guid.NewGuid(), desastre, (NivelDano)(i % 5), 1, 1, T0,
                i % 2 == 0 ? "63001" : "63130", lon: -75.70 + i % 100 * 0.001)));
        string ruta = $"/v1/consulta-danos/mapa?desastre={desastre}&puntos=true";

        System.Diagnostics.Stopwatch reloj = System.Diagnostics.Stopwatch.StartNew();
        JsonElement mapa = await GetAsync(ruta);
        long desdeLaBase = reloj.ElapsedMilliseconds;
        reloj.Restart();
        await GetAsync(ruta);
        long desdeLaCache = reloj.ElapsedMilliseconds;

        Assert.Equal(5000, Features(mapa, "activo").Count());
        Assert.Equal(5000, mapa.GetProperty("resumen").GetProperty("activos").GetInt32());
        Assert.True(desdeLaBase < 2000, $"Desde la base: {desdeLaBase} ms");
        Assert.True(desdeLaCache < desdeLaBase, $"Desde la caché: {desdeLaCache} ms; desde la base: {desdeLaBase} ms");
        _salida.WriteLine($"QA-06 (referencia): mapa de 5000 activos con puntos, desde la base {desdeLaBase} ms, desde la caché {desdeLaCache} ms");
    }

    [Fact]
    public async Task De_punta_a_punta_un_evento_publicado_en_el_bus_llega_a_la_proyeccion()
    {
        Guid desastre = Guid.NewGuid(), activo = Guid.NewGuid();
        await _e.PublicarAsync(Sincronizada(activo, Guid.NewGuid(), desastre, NivelDano.GRAVE, 1, 1, T0));
        DateTime limite = DateTime.UtcNow.AddSeconds(60);
        HttpStatusCode estado;
        do
        {
            estado = (await _e.Cliente.GetAsync($"/v1/consulta-danos/activos/{activo}/evolucion")).StatusCode;
            if (estado == HttpStatusCode.OK) break;
            await Task.Delay(250);
        } while (DateTime.UtcNow < limite);
        Assert.Equal(HttpStatusCode.OK, estado);
    }

    [Fact]
    public async Task Si_Redis_cae_el_mapa_se_arma_desde_PostGIS_y_la_cache_informa_degradada()
    {
        Guid desastre = Guid.NewGuid();
        await _e.EntregarAsync(Sincronizada(Guid.NewGuid(), Guid.NewGuid(), desastre, NivelDano.GRAVE, 1, 1, T0));
        await using WebApplicationFactory<Program> sinCache = _e.Fabrica(_e.App, EntornoConsultaDanos.Caido, _e.NodoBus);
        using HttpClient cliente = EntornoConsultaDanos.ConIdentidad(sinCache.CreateClient());

        HttpResponseMessage r = await cliente.GetAsync($"/v1/consulta-danos/mapa?desastre={desastre}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        JsonElement mapa = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(mapa.GetProperty("degradado").GetBoolean());
        Assert.Equal("Polygon", Zona(mapa, "63001").GetProperty("geometry").GetProperty("type").GetString());

        // La caché no es parte de la preparación: el sondeo informa Degraded (200), no Unhealthy.
        HttpResponseMessage salud = await cliente.GetAsync("/salud/listo");
        JsonElement cuerpo = await salud.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Degraded", cuerpo.GetProperty("dependencias").GetProperty("cache").GetProperty("estado").GetString());
        Assert.Equal("Healthy", cuerpo.GetProperty("dependencias").GetProperty("base").GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);
    }

    [Fact]
    public async Task Si_PostGIS_cae_se_sirve_el_ultimo_mapa_con_degradado_true_CON6()
    {
        Guid desastre = Guid.NewGuid();
        await _e.EntregarAsync(Sincronizada(Guid.NewGuid(), Guid.NewGuid(), desastre, NivelDano.GRAVE, 1, 1, T0.AddMinutes(7)));
        string ruta = $"/v1/consulta-danos/mapa?desastre={desastre}&puntos=true";
        await GetAsync(ruta);                                              // la réplica sana lo arma y lo guarda
        await GetAsync($"/v1/consulta-danos/consolidado?desastre={desastre}");

        string baseCaida = new NpgsqlConnectionStringBuilder(_e.App) { Host = "127.0.0.1", Port = 1, Timeout = 2 }.ConnectionString;
        await using WebApplicationFactory<Program> sinBase = _e.Fabrica(baseCaida, _e.Redis, EntornoConsultaDanos.Caido);
        using HttpClient cliente = EntornoConsultaDanos.ConIdentidad(sinBase.CreateClient());

        // Mientras la respuesta armada siga vigente en la caché, se sirve tal cual: es el dato actual.
        HttpResponseMessage enCache = await cliente.GetAsync(ruta);
        Assert.Equal("false", enCache.Headers.GetValues("X-Degradado").Single());

        // Cuando expira, la base no responde y se sirve la última conocida, marcada.
        await _e.ExpirarRespuestasAsync(desastre);
        HttpResponseMessage r = await cliente.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("true", r.Headers.GetValues("X-Degradado").Single());
        JsonElement mapa = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(mapa.GetProperty("degradado").GetBoolean());
        Assert.Equal(T0.AddMinutes(7), mapa.GetProperty("datoAl").GetDateTimeOffset());
        Assert.Single(Features(mapa, "activo"));

        JsonElement consolidado = await cliente.GetFromJsonAsync<JsonElement>($"/v1/consulta-danos/consolidado?desastre={desastre}");
        Assert.True(consolidado.GetProperty("degradado").GetBoolean());

        // Un mapa que nunca se armó no tiene respaldo: 503 para que el cliente reintente.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await cliente.GetAsync($"/v1/consulta-danos/mapa?desastre={Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("/v1/consulta-danos/mapa?desastre=11111111-1111-1111-1111-111111111111")]
    [InlineData("/v1/consulta-danos/consolidado?desastre=11111111-1111-1111-1111-111111111111")]
    [InlineData("/v1/consulta-danos/activos/11111111-1111-1111-1111-111111111111/evolucion")]
    [InlineData("/v1/consulta-danos/indicadores-revision")]
    public async Task Sin_identidad_la_consulta_responde_401(string ruta)
    {
        using HttpClient sinIdentidad = _e.SinIdentidad();
        Assert.Equal(HttpStatusCode.Unauthorized, (await sinIdentidad.GetAsync(ruta)).StatusCode);
    }

    [Theory]
    [InlineData("/v1/consulta-danos/mapa")]
    [InlineData("/v1/consulta-danos/mapa?desastre=11111111-1111-1111-1111-111111111111&nivelMinimo=MUY_GRAVE")]
    [InlineData("/v1/consulta-danos/consolidado")]
    public async Task Un_parametro_invalido_responde_400(string ruta) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _e.Cliente.GetAsync(ruta)).StatusCode);

    [Fact]
    public async Task Un_activo_sin_evaluaciones_responde_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _e.Cliente.GetAsync($"/v1/consulta-danos/activos/{Guid.NewGuid()}/evolucion")).StatusCode);

    [Theory]
    [InlineData("DELETE FROM p_evaluacion_vigente")]
    [InlineData("DELETE FROM p_dano_por_zona")]
    [InlineData("UPDATE p_historial_activo SET nivel_dano = 'LEVE'")]
    [InlineData("DELETE FROM mensajes_procesados")]
    [InlineData("DROP TABLE p_municipios")]
    public async Task El_servicio_no_puede_borrar_cambiar_el_historial_ni_el_esquema(string sql)
    {
        await using NpgsqlConnection c = new(_e.App);
        await c.OpenAsync();
        await using NpgsqlCommand comando = new(sql, c);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
