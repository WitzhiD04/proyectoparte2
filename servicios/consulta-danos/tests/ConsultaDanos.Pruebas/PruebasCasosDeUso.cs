using System.Reflection;
using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using static ConsultaDanos.Pruebas.Hechos;

namespace ConsultaDanos.Pruebas;

/// <summary>
/// El caso de uso con el almacén en memoria: dos réplicas consumen la misma cola, así que cualquier orden de llegada es
/// posible. Se prueban todos los órdenes y, además, cada evento repetido.
/// </summary>
public class PruebasProyecciones
{
    private static async Task AplicarAsync(ActualizarProyecciones caso, object hecho)
    {
        Task t = hecho switch
        {
            EvaluacionRegistrada h => caso.AplicarAsync(h, default),
            ValidacionCambiada h => caso.AplicarAsync(h, default),
            ConflictoDetectado h => caso.AplicarAsync(h, default),
            ConflictoResuelto h => caso.AplicarAsync(h, default),
            _ => throw new ArgumentException(hecho.GetType().Name),
        };
        await t;
    }

    [Fact]
    public async Task Cualquier_orden_y_cualquier_repeticion_dejan_la_misma_proyeccion()
    {
        Guid activo = Guid.NewGuid();
        Guid evaluacion = Guid.NewGuid();
        // Sincronización (v1, leve), validación (v2), edición sin conexión (v3, grave) que retira la validación (EV-08
        // PENDIENTE con la misma versión, E-02) y una validación nueva (v4).
        object[] eventos =
        [
            Sincronizada(activo, Contenido(evaluacion, NivelDano.LEVE, 1), 1, 0),
            Validacion(activo, Contenido(evaluacion, NivelDano.LEVE, 2, estado: "VALIDADA"), EstadoValidacion.VALIDADA, 1),
            Sincronizada(activo, Contenido(evaluacion, NivelDano.GRAVE, 3), 3, 2),
            Validacion(activo, Contenido(evaluacion, NivelDano.GRAVE, 3), EstadoValidacion.PENDIENTE, 2),
            Validacion(activo, Contenido(evaluacion, NivelDano.GRAVE, 4, estado: "VALIDADA"), EstadoValidacion.VALIDADA, 3),
        ];
        ClaveZona grave = new(Desastre, "63001", "VIVIENDA", NivelDano.GRAVE);

        int ordenes = 0;
        foreach (IReadOnlyList<object> orden in Permutaciones(eventos))
        {
            AlmacenEnMemoria almacen = new();
            ActualizarProyecciones caso = new(almacen, new InvalidacionEnMemoria());
            foreach (object e in orden.Concat(orden.Take(2)))   // los dos primeros llegan otra vez
            {
                await AplicarAsync(caso, e);
            }
            EstadoActivo final = almacen.Activos[activo];
            Assert.Equal(NivelDano.GRAVE, final.Contenido!.Nivel);
            Assert.Equal(3, final.VersionActivo);
            Assert.True(final.Validada);
            Assert.Equal(1, almacen.Consolidado[grave]);
            Assert.Equal(1, almacen.Consolidado.Values.Sum());
            Assert.Equal(5, almacen.Historial.Count);
            ordenes++;
        }
        Assert.Equal(120, ordenes);
    }

    [Fact]
    public async Task Una_evaluacion_entra_al_consolidado_al_validarse_y_sale_al_volver_a_pendiente()
    {
        AlmacenEnMemoria almacen = new();
        InvalidacionEnMemoria invalidacion = new();
        ActualizarProyecciones caso = new(almacen, invalidacion);
        Guid activo = Guid.NewGuid(), evaluacion = Guid.NewGuid();
        ClaveZona clave = new(Desastre, "63001", "VIVIENDA", NivelDano.MODERADO);

        await caso.AplicarAsync(Sincronizada(activo, Contenido(evaluacion, NivelDano.MODERADO, 1), 1, 0), default);
        Assert.DoesNotContain(almacen.Consolidado, c => c.Value > 0);
        Assert.Contains(Desastre, invalidacion.Desastres);   // el mapa sin filtro sí cambia

        await caso.AplicarAsync(Validacion(activo, Contenido(evaluacion, NivelDano.MODERADO, 2), EstadoValidacion.VALIDADA, 1), default);
        Assert.Equal(1, almacen.Consolidado[clave]);

        await caso.AplicarAsync(Validacion(activo, Contenido(evaluacion, NivelDano.MODERADO, 3), EstadoValidacion.PENDIENTE, 2), default);
        Assert.Equal(0, almacen.Consolidado[clave]);

        await caso.AplicarAsync(Validacion(activo, Contenido(evaluacion, NivelDano.MODERADO, 4), EstadoValidacion.RECHAZADA, 3), default);
        Assert.Equal(0, almacen.Consolidado[clave]);
    }

    [Fact]
    public async Task Un_duplicado_fusionado_deja_de_contar_lleguen_como_lleguen_los_eventos_E14()
    {
        Guid vigente = Guid.NewGuid(), duplicado = Guid.NewGuid(), evVigente = Guid.NewGuid(), evDuplicado = Guid.NewGuid();
        Guid conflicto = Guid.NewGuid();
        ContenidoActivo cVigente = Contenido(evVigente, NivelDano.GRAVE, 2, tipo: "ESCUELA");
        object[] eventos =
        [
            Sincronizada(vigente, Contenido(evVigente, NivelDano.GRAVE, 1, tipo: "ESCUELA"), 1, 0),
            Validacion(vigente, cVigente, EstadoValidacion.VALIDADA, 1),
            Sincronizada(duplicado, Contenido(evDuplicado, NivelDano.GRAVE, 1, tipo: "ESCUELA"), 1, 2, enConflicto: true),
            new ConflictoDetectado(conflicto, duplicado, "DUPLICADO", evDuplicado, T0.AddMinutes(2)),
            new ConflictoResuelto(conflicto, ConflictoResuelto.Fusionar, "operador.danos", "Es la misma escuela", vigente,
                cVigente with { VersionEvaluacion = 3 }, 2, T0.AddMinutes(3)),
        ];
        ClaveZona clave = new(Desastre, "63001", "ESCUELA", NivelDano.GRAVE);

        foreach (IReadOnlyList<object> orden in Permutaciones(eventos))
        {
            AlmacenEnMemoria almacen = new();
            ActualizarProyecciones caso = new(almacen, new InvalidacionEnMemoria());
            foreach (object e in orden) await AplicarAsync(caso, e);

            Assert.True(almacen.Activos[duplicado].Fusionado);
            Assert.Equal(vigente, almacen.Activos[duplicado].FusionadoEn);
            Assert.False(almacen.Activos[vigente].Fusionado);
            Assert.Equal(1, almacen.Consolidado[clave]);
            Assert.Equal(0, almacen.Pendientes);
        }
    }

    [Fact]
    public async Task La_bandeja_cuenta_los_pendientes_y_el_mas_viejo_R08()
    {
        AlmacenEnMemoria almacen = new();
        ActualizarProyecciones caso = new(almacen, new InvalidacionEnMemoria());
        Guid c1 = Guid.NewGuid(), c2 = Guid.NewGuid(), activo = Guid.NewGuid(), evaluacion = Guid.NewGuid();
        await caso.AplicarAsync(new ConflictoDetectado(c1, activo, "VERSION", null, T0), default);
        await caso.AplicarAsync(new ConflictoDetectado(c2, activo, "VERSION", null, T0.AddHours(1)), default);
        Assert.Equal(2, almacen.Pendientes);
        Assert.Equal(T0, almacen.MasViejo);

        await caso.AplicarAsync(new ConflictoResuelto(c1, "PREVALECE_VIGENTE", "operador.danos", "ok", activo,
            Contenido(evaluacion, NivelDano.LEVE, 2), 1, T0.AddHours(2)), default);
        Assert.Equal(1, almacen.Pendientes);
        Assert.Equal(T0.AddHours(1), almacen.MasViejo);

        // EV-10 repetido o tardío no reabre un conflicto resuelto.
        await caso.AplicarAsync(new ConflictoDetectado(c1, activo, "VERSION", null, T0), default);
        Assert.Equal(1, almacen.Pendientes);
    }
}

public class PruebasMapa
{
    private static readonly FiltroMapa Filtro = new(Desastre, NivelDano.SIN_DANO, false, true);

    private sealed class Lecturas : ILecturasDanos
    {
        public bool Falla { get; set; }
        public int Armados { get; private set; }
        public int GeometriasLeidas { get; private set; }

        public Task<IReadOnlyList<ZonaMapa>> ZonasMapaAsync(FiltroMapa filtro, CancellationToken ct)
        {
            if (Falla) throw new InvalidOperationException("La base no responde");
            Armados++;
            return Task.FromResult<IReadOnlyList<ZonaMapa>>(
                [new ZonaMapa("63001", "Armenia", "63", 1, T0, new Distribucion().Sumar(NivelDano.GRAVE, 2), 0)]);
        }

        public Task<IReadOnlyList<PuntoMapa>> PuntosAsync(FiltroMapa filtro, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PuntoMapa>>([]);

        public Task<IReadOnlyDictionary<string, string>> GeometriasAsync(IReadOnlyCollection<string> codigos, CancellationToken ct)
        {
            GeometriasLeidas += codigos.Count;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(codigos.ToDictionary(c => c, _ => "{\"type\":\"Polygon\"}"));
        }

        public Task<DateTimeOffset?> DatoAlAsync(Guid desastre, CancellationToken ct) => Task.FromResult<DateTimeOffset?>(T0.AddMinutes(5));
        public Task<IReadOnlyList<ZonaConsolidada>> ConsolidadoAsync(Guid desastre, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<EvolucionActivo?> EvolucionAsync(Guid idActivo, CancellationToken ct) => throw new NotImplementedException();
        public Task<BandejaLeida> BandejaAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class Cache : ICacheDanos
    {
        public Dictionary<string, string> Valores { get; } = new();
        public bool Falla { get; set; }
        public long Version { get; set; }

        private void Fallar()
        {
            if (Falla) throw new InvalidOperationException("Redis no responde");
        }

        public Task<string?> LeerAsync(string clave, CancellationToken ct)
        {
            Fallar();
            return Task.FromResult(Valores.GetValueOrDefault(clave));
        }

        public Task<IReadOnlyList<string?>> LeerVariasAsync(IReadOnlyList<string> claves, CancellationToken ct)
        {
            Fallar();
            return Task.FromResult<IReadOnlyList<string?>>(claves.Select(c => Valores.GetValueOrDefault(c)).ToList());
        }

        public Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct)
        {
            Fallar();
            Valores[clave] = valor;
            return Task.CompletedTask;
        }

        public Task<(long Global, long Desastre)> VersionesAsync(Guid desastre, CancellationToken ct)
        {
            Fallar();
            return Task.FromResult((0L, Version));
        }

        public Task InvalidarAsync(IReadOnlyCollection<Guid> desastres, bool global, CancellationToken ct)
        {
            Version++;
            return Task.CompletedTask;
        }
    }

    private sealed class Formato : IFormatoDanos
    {
        public string Mapa(FiltroMapa filtro, IReadOnlyList<ZonaMapa> zonas, IReadOnlyDictionary<string, string> geometrias,
            IReadOnlyList<PuntoMapa> puntos, DateTimeOffset? datoAl) => $"mapa:{zonas.Count}:{geometrias.Count}:{datoAl:HH:mm}";
        public string Consolidado(Consolidado consolidado) => "consolidado";
        public string MarcarDegradado(string respuesta) => respuesta + ":degradado";
    }

    private static ConsultarMapa Caso(Lecturas l, Cache c) => new(l, c, new Formato(), ParametrosConsulta.PorOmision);

    [Fact]
    public async Task La_segunda_consulta_sale_de_la_cache_y_un_evento_del_desastre_la_invalida_E21()
    {
        Lecturas lecturas = new();
        Cache cache = new();
        ConsultarMapa caso = Caso(lecturas, cache);

        RespuestaLectura primera = await caso.EjecutarAsync(Filtro, default);
        await caso.EjecutarAsync(Filtro, default);
        Assert.Equal(1, lecturas.Armados);
        Assert.Equal("mapa:1:1:10:05", primera.Contenido);   // la geometría y el datoAl (el más reciente)
        Assert.Contains($"geo:MUNICIPIOS:1:63001", cache.Valores.Keys);

        await cache.InvalidarAsync([Desastre], false, default);
        await caso.EjecutarAsync(Filtro, default);
        Assert.Equal(2, lecturas.Armados);
        Assert.Equal(1, lecturas.GeometriasLeidas);   // la geometría ya estaba en la caché por capa y versión
    }

    [Fact]
    public async Task Si_la_cache_cae_se_arma_desde_la_base_sin_degradar_CON6()
    {
        Lecturas lecturas = new();
        RespuestaLectura r = await Caso(lecturas, new Cache { Falla = true }).EjecutarAsync(Filtro, default);
        Assert.False(r.Degradado);
        Assert.Equal("mapa:1:1:10:05", r.Contenido);
    }

    [Fact]
    public async Task Si_la_base_cae_se_sirve_el_ultimo_mapa_marcado_como_degradado_CON6()
    {
        Lecturas lecturas = new();
        Cache cache = new();
        ConsultarMapa caso = Caso(lecturas, cache);
        await caso.EjecutarAsync(Filtro, default);

        lecturas.Falla = true;
        await cache.InvalidarAsync([Desastre], false, default);
        RespuestaLectura r = await caso.EjecutarAsync(Filtro, default);
        Assert.True(r.Degradado);
        Assert.Equal("mapa:1:1:10:05:degradado", r.Contenido);
    }

    [Fact]
    public async Task Si_caen_la_base_y_la_cache_no_hay_con_que_responder()
    {
        ConsultarMapa caso = Caso(new Lecturas { Falla = true }, new Cache { Falla = true });
        await Assert.ThrowsAsync<DatosNoDisponiblesException>(() => caso.EjecutarAsync(Filtro, default));
    }

    [Fact]
    public void La_clave_del_mapa_distingue_los_filtros()
    {
        Assert.NotEqual(Filtro.Clave, (Filtro with { Puntos = false }).Clave);
        Assert.NotEqual(Filtro.Clave, (Filtro with { SoloValidadas = true }).Clave);
        Assert.Equal([NivelDano.GRAVE, NivelDano.DESTRUCCION_TOTAL], (Filtro with { NivelMinimo = NivelDano.GRAVE }).Niveles);
    }
}

public class PruebasArquitectura
{
    private static IEnumerable<string> Referencias(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name ?? "");

    private static readonly string[] Prohibidos =
    [
        "Plataforma.Comun", "Microsoft.EntityFrameworkCore", "Npgsql", "NetTopologySuite", "StackExchange", "RabbitMQ",
        "Microsoft.AspNetCore", "ConsultaDanos.Infraestructura", "ConsultaDanos.Api", "System.Text.Json",
    ];

    [Fact]
    public void El_dominio_no_depende_de_otros_proyectos_ni_de_paquetes()
    {
        Assembly dominio = typeof(EstadoActivo).Assembly;
        Assert.DoesNotContain(Referencias(dominio), r => r.StartsWith("ConsultaDanos.", StringComparison.Ordinal));
        Assert.DoesNotContain(Referencias(dominio), r => Prohibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void La_aplicacion_solo_depende_del_dominio()
    {
        Assembly aplicacion = typeof(ActualizarProyecciones).Assembly;
        Assert.Equal(["ConsultaDanos.Dominio"], Referencias(aplicacion).Where(r => r.StartsWith("ConsultaDanos.", StringComparison.Ordinal)));
        Assert.DoesNotContain(Referencias(aplicacion), r => Prohibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }
}
