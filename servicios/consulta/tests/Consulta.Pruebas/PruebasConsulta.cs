using System.Reflection;
using Consulta.Aplicacion;
using Consulta.Dominio;

namespace Consulta.Pruebas;

public class PruebasEvolucion
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void El_area_de_un_instante_pasado_es_la_version_vigente_entonces_RFGD09()
    {
        // A las 10:00 la zona son 3 barrios (v1); a las 14:00, 7 (v2): el ejemplo del enunciado §4.1.
        ZonaLeida v1 = new(Id, 1, T0, "{\"type\":\"Polygon\"}", ["63001"], "DecisionAutoridad");
        ZonaLeida v2 = new(Id, 2, T0.AddHours(4), "{\"type\":\"Polygon\"}", ["63001", "63130"], "EvaluacionCampo");
        CambioEvolucion[] cambios =
        [
            new(Id, T0, "REGISTRO", null, "NIVEL_2|REGISTRADO", "a", 1),
            new(Id, T0.AddHours(2), "SEVERIDAD", "NIVEL_2", "NIVEL_3", "a", 2),
            new(Id, T0.AddHours(5), "ESTADO", "REGISTRADO", "EN_ATENCION", "a", 3),
        ];

        EstadoEnInstante alas13 = Evolucion.En(T0.AddHours(3), cambios, [v1, v2]);
        Assert.Equal("NIVEL_3", alas13.Severidad);
        Assert.Equal("REGISTRADO", alas13.Estado);
        Assert.Equal(1, alas13.Zona!.Version);

        EstadoEnInstante alas15 = Evolucion.En(T0.AddHours(5), cambios, [v1, v2]);
        Assert.Equal("EN_ATENCION", alas15.Estado);
        Assert.Equal(2, alas15.Zona!.Version);
    }
}

public class PruebasMapa
{
    private static readonly FilaTablero Desastre = new(Guid.NewGuid(), "SISMO", "Sismo", "NIVEL_3", "EN_ATENCION", "ungrd", 1, 1, DateTimeOffset.UtcNow);
    private static readonly ZonaLeida Zona = new(Desastre.Id, 1, DateTimeOffset.UtcNow, "{\"type\":\"Polygon\",\"coordinates\":[]}", ["63001"], "x");

    private sealed class Lecturas : ILecturas
    {
        public bool Falla { get; set; }
        public int Llamadas { get; private set; }

        public Task<IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)>> MapaAsync(bool soloActivos, CancellationToken ct)
        {
            Llamadas++;
            if (Falla) throw new InvalidOperationException("La base no responde");
            return Task.FromResult<IReadOnlyList<(FilaTablero, ZonaLeida)>>([(Desastre, Zona)]);
        }

        public Task<IReadOnlyList<FilaTablero>> TableroAsync(string? e, string? t, CancellationToken ct) => throw new NotImplementedException();
        public Task<VistaDesastre?> DesastreAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<(IReadOnlyList<CambioEvolucion> Cambios, IReadOnlyList<ZonaLeida> Zonas)?> EvolucionAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class Cache : ICacheConsulta
    {
        public Dictionary<string, string> Valores { get; } = new();
        public bool Falla { get; set; }
        public long Version { get; set; }

        public Task<string?> LeerAsync(string clave, CancellationToken ct) =>
            Falla ? throw new InvalidOperationException("Redis no responde") : Task.FromResult(Valores.GetValueOrDefault(clave));

        public Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct)
        {
            if (Falla) throw new InvalidOperationException("Redis no responde");
            Valores[clave] = valor;
            return Task.CompletedTask;
        }

        public Task<long> VersionMapaAsync(CancellationToken ct) => Falla ? throw new InvalidOperationException("Redis no responde") : Task.FromResult(Version);
        public Task InvalidarMapaAsync(CancellationToken ct) { Version++; return Task.CompletedTask; }
    }

    private sealed class Formato : IFormatoMapa
    {
        public string Serializar(IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)> zonas, DateTimeOffset? datoAl, bool degradado) => $"mapa:{zonas.Count}";
        public string MarcarDegradado(string mapa) => mapa + ":degradado";
    }

    [Fact]
    public async Task La_segunda_consulta_sale_de_la_cache_y_un_cambio_la_invalida_AD17()
    {
        Lecturas lecturas = new();
        Cache cache = new();
        ConsultarMapa caso = new(lecturas, cache, new Formato());

        await caso.EjecutarAsync(true, default);
        await caso.EjecutarAsync(true, default);
        Assert.Equal(1, lecturas.Llamadas);

        await cache.InvalidarMapaAsync(default);
        await caso.EjecutarAsync(true, default);
        Assert.Equal(2, lecturas.Llamadas);
    }

    [Fact]
    public async Task Si_la_cache_cae_se_arma_desde_la_base()
    {
        Lecturas lecturas = new();
        ConsultarMapa caso = new(lecturas, new Cache { Falla = true }, new Formato());
        (string mapa, bool degradado) = await caso.EjecutarAsync(true, default);
        Assert.Equal("mapa:1", mapa);
        Assert.False(degradado);
    }

    [Fact]
    public async Task Si_la_base_cae_se_sirve_el_ultimo_mapa_marcado_como_degradado_CON6()
    {
        Lecturas lecturas = new();
        Cache cache = new();
        ConsultarMapa caso = new(lecturas, cache, new Formato());
        await caso.EjecutarAsync(true, default);

        lecturas.Falla = true;
        await cache.InvalidarMapaAsync(default);
        (string mapa, bool degradado) = await caso.EjecutarAsync(true, default);
        Assert.True(degradado);
        Assert.Equal("mapa:1:degradado", mapa);
    }
}

public class PruebasArquitectura
{
    private static IEnumerable<string> Referencias(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name ?? "");
    private static readonly string[] Prohibidos = ["Plataforma.Comun", "Microsoft.EntityFrameworkCore", "Npgsql", "NetTopologySuite", "StackExchange", "RabbitMQ",
        "Microsoft.AspNetCore", "Consulta.Infraestructura", "Consulta.Api", "System.Text.Json"];

    [Fact]
    public void Dominio_y_aplicacion_no_dependen_de_infraestructura()
    {
        Assert.DoesNotContain(Referencias(typeof(Evolucion).Assembly), r => r.StartsWith("Consulta.", StringComparison.Ordinal) || Prohibidos.Any(r.StartsWith));
        Assert.Equal(["Consulta.Dominio"], Referencias(typeof(ConsultarMapa).Assembly).Where(r => r.StartsWith("Consulta.", StringComparison.Ordinal)));
        Assert.DoesNotContain(Referencias(typeof(ConsultarMapa).Assembly), r => Prohibidos.Any(r.StartsWith));
    }
}
