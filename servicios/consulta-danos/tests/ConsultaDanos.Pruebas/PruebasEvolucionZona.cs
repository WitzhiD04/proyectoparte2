using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using static ConsultaDanos.Pruebas.Hechos;

namespace ConsultaDanos.Pruebas;

/// <summary>Evolución de una zona (RF-ED-15, E-40) con las lecturas en memoria.</summary>
public class PruebasEvolucionZona
{
    private static readonly Guid OtroDesastre = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Dia1 = DateOnly.FromDateTime(T0.UtcDateTime);
    private static readonly DateOnly Dia2 = Dia1.AddDays(1);

    private readonly Lecturas _lecturas = new();
    private ConsultarEvolucionZona Caso => new(_lecturas);

    private static EntradaHistorial Entrada(Guid activo, Guid evaluacion, NivelDano nivel, DateTimeOffset instante, string municipio = "63001",
        Guid? desastre = null, string origen = OrigenHistorial.Sincronizacion, EstadoValidacion? validacion = null) =>
        new(activo, evaluacion, 1, origen, instante, nivel, "VIVIENDA", "REGISTRADA", validacion, false, desastre ?? Desastre, municipio,
            "evaluador.armenia", null);

    [Theory]
    [InlineData("63001", TipoZona.MUNICIPIO)]
    [InlineData("63", TipoZona.DEPARTAMENTO)]
    public void Un_codigo_de_5_digitos_es_municipio_y_uno_de_2_departamento(string codigo, TipoZona tipo) =>
        Assert.Equal(tipo, CodigoZona.TipoDe(codigo));

    [Theory]
    [InlineData("")]
    [InlineData("6")]
    [InlineData("630")]
    [InlineData("6300")]
    [InlineData("630011")]
    [InlineData("6300a")]
    [InlineData("٦٣")]
    public void Otro_codigo_no_es_una_zona(string codigo) => Assert.Null(CodigoZona.TipoDe(codigo));

    [Fact]
    public async Task Un_codigo_invalido_no_llega_a_las_lecturas() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Caso.EjecutarAsync("6300", null, default));

    [Fact]
    public async Task La_evolucion_de_un_municipio_trae_su_nombre_sus_entradas_y_la_ultima_fecha()
    {
        Guid activo = Guid.NewGuid();
        _lecturas.Historial.Add(Entrada(activo, Guid.NewGuid(), NivelDano.LEVE, T0));
        _lecturas.Historial.Add(Entrada(activo, Guid.NewGuid(), NivelDano.GRAVE, T0.AddMinutes(5)));
        _lecturas.Historial.Add(Entrada(Guid.NewGuid(), Guid.NewGuid(), NivelDano.GRAVE, T0.AddMinutes(9), "63130"));

        EvolucionZona e = (await Caso.EjecutarAsync("63001", null, default))!;

        Assert.Equal(new ZonaConsultada("63001", TipoZona.MUNICIPIO, "Armenia"), e.Zona);
        Assert.Equal([NivelDano.LEVE, NivelDano.GRAVE], e.Entradas.Select(x => x.Nivel));
        Assert.Equal(T0.AddMinutes(5), e.DatoAl);
    }

    [Fact]
    public async Task La_evolucion_de_un_departamento_reune_sus_municipios_y_trae_su_nombre()
    {
        _lecturas.Historial.Add(Entrada(Guid.NewGuid(), Guid.NewGuid(), NivelDano.LEVE, T0));
        _lecturas.Historial.Add(Entrada(Guid.NewGuid(), Guid.NewGuid(), NivelDano.GRAVE, T0.AddMinutes(5), "63130"));

        EvolucionZona e = (await Caso.EjecutarAsync("63", null, default))!;

        Assert.Equal(new ZonaConsultada("63", TipoZona.DEPARTAMENTO, "QUINDÍO"), e.Zona);
        Assert.Equal(["63001", "63130"], e.Entradas.Select(x => x.CodigoMunicipio));
    }

    [Fact]
    public async Task El_filtro_por_desastre_deja_solo_sus_entradas_y_su_fecha()
    {
        _lecturas.Historial.Add(Entrada(Guid.NewGuid(), Guid.NewGuid(), NivelDano.LEVE, T0));
        _lecturas.Historial.Add(Entrada(Guid.NewGuid(), Guid.NewGuid(), NivelDano.GRAVE, T0.AddMinutes(5), desastre: OtroDesastre));

        EvolucionZona e = (await Caso.EjecutarAsync("63001", OtroDesastre, default))!;

        Assert.Equal(OtroDesastre, Assert.Single(e.Entradas).IdDesastre);
        Assert.Equal(T0.AddMinutes(5), e.DatoAl);
        Assert.Equal(1, Assert.Single(e.PorDia).Distribucion[NivelDano.GRAVE]);
    }

    [Theory]
    [InlineData("99999")]
    [InlineData("11")]
    public async Task Una_zona_que_no_esta_en_la_capa_no_existe_y_no_se_lee_su_historial(string codigo)
    {
        Assert.Null(await Caso.EjecutarAsync(codigo, null, default));
        Assert.Equal(0, _lecturas.LecturasHistorial);
    }

    [Fact]
    public async Task Una_zona_sin_evaluaciones_viene_vacia_y_sin_fecha()
    {
        EvolucionZona e = (await Caso.EjecutarAsync("63001", null, default))!;
        Assert.Empty(e.Entradas);
        Assert.Empty(e.PorDia);
        Assert.Null(e.DatoAl);
    }

    [Fact]
    public async Task Por_dia_cada_activo_cuenta_una_vez_en_su_ultimo_nivel_de_ese_dia()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        _lecturas.Historial.Add(Entrada(a, Guid.NewGuid(), NivelDano.LEVE, T0));
        _lecturas.Historial.Add(Entrada(b, Guid.NewGuid(), NivelDano.MODERADO, T0.AddMinutes(1)));
        _lecturas.Historial.Add(Entrada(a, Guid.NewGuid(), NivelDano.GRAVE, T0.AddMinutes(2)));
        _lecturas.Historial.Add(Entrada(a, Guid.NewGuid(), NivelDano.DESTRUCCION_TOTAL, T0.AddDays(1)));

        IReadOnlyList<ConteoDia> dias = (await Caso.EjecutarAsync("63001", null, default))!.PorDia;

        Assert.Equal([Dia1, Dia2], dias.Select(d => d.Fecha));
        Assert.Equal(2, dias[0].Distribucion.Total);
        Assert.Equal(0, dias[0].Distribucion[NivelDano.LEVE]);
        Assert.Equal(1, dias[0].Distribucion[NivelDano.MODERADO]);
        Assert.Equal(1, dias[0].Distribucion[NivelDano.GRAVE]);
        Assert.Equal(1, dias[1].Distribucion.Total);
        Assert.Equal(1, dias[1].Distribucion[NivelDano.DESTRUCCION_TOTAL]);
    }

    [Fact]
    public async Task El_dia_se_corta_en_UTC_aunque_el_instante_venga_con_otra_zona_horaria()
    {
        DateTimeOffset nocheEnBogota = new(2026, 11, 16, 20, 0, 0, TimeSpan.FromHours(-5));
        _lecturas.Historial.Add(Entrada(Guid.NewGuid(), Guid.NewGuid(), NivelDano.LEVE, nocheEnBogota));

        ConteoDia dia = Assert.Single((await Caso.EjecutarAsync("63001", null, default))!.PorDia);

        Assert.Equal(new DateOnly(2026, 11, 17), dia.Fecha);
    }

    [Fact]
    public async Task Una_evaluacion_rechazada_no_cuenta_desde_ese_dia_pero_sigue_en_la_secuencia()
    {
        Guid activo = Guid.NewGuid(), buena = Guid.NewGuid(), rechazada = Guid.NewGuid();
        _lecturas.Historial.Add(Entrada(activo, buena, NivelDano.LEVE, T0));
        _lecturas.Historial.Add(Entrada(activo, rechazada, NivelDano.GRAVE, T0.AddMinutes(1)));
        _lecturas.Historial.Add(Entrada(activo, rechazada, NivelDano.GRAVE, T0.AddMinutes(2), origen: OrigenHistorial.Validacion,
            validacion: EstadoValidacion.RECHAZADA));

        EvolucionZona e = (await Caso.EjecutarAsync("63001", null, default))!;

        Assert.Equal(3, e.Entradas.Count);
        Distribucion dia = Assert.Single(e.PorDia).Distribucion;
        Assert.Equal(1, dia[NivelDano.LEVE]);
        Assert.Equal(0, dia[NivelDano.GRAVE]);
    }

    [Fact]
    public async Task Un_rechazo_posterior_no_cambia_lo_que_ya_se_conto_en_un_dia_anterior()
    {
        Guid activo = Guid.NewGuid(), evaluacion = Guid.NewGuid();
        _lecturas.Historial.Add(Entrada(activo, evaluacion, NivelDano.GRAVE, T0));
        _lecturas.Historial.Add(Entrada(activo, evaluacion, NivelDano.GRAVE, T0.AddDays(1), origen: OrigenHistorial.Validacion,
            validacion: EstadoValidacion.RECHAZADA));

        IReadOnlyList<ConteoDia> dias = (await Caso.EjecutarAsync("63001", null, default))!.PorDia;

        Assert.Equal(1, dias[0].Distribucion[NivelDano.GRAVE]);
        Assert.Equal(0, dias[1].Distribucion.Total);
    }

    [Fact]
    public async Task Si_la_base_no_responde_los_datos_no_estan_disponibles()
    {
        _lecturas.Falla = true;
        await Assert.ThrowsAsync<DatosNoDisponiblesException>(() => Caso.EjecutarAsync("63001", null, default));
    }

    /// <summary>Lecturas con la capa de dos municipios del Quindío y el historial en memoria.</summary>
    private sealed class Lecturas : ILecturasDanos
    {
        private readonly Dictionary<string, string> _municipios = new() { ["63001"] = "Armenia", ["63130"] = "Calarcá" };
        public List<EntradaHistorial> Historial { get; } = [];
        public int LecturasHistorial { get; private set; }
        public bool Falla { get; set; }

        private void Fallar()
        {
            if (Falla) throw new InvalidOperationException("La base no responde");
        }

        public Task<string?> NombreMunicipioAsync(string codigo, CancellationToken ct)
        {
            Fallar();
            return Task.FromResult(_municipios.GetValueOrDefault(codigo));
        }

        public Task<bool> DepartamentoEnCapaAsync(string codigo, CancellationToken ct)
        {
            Fallar();
            return Task.FromResult(_municipios.Keys.Any(m => m.StartsWith(codigo, StringComparison.Ordinal)));
        }

        public Task<IReadOnlyList<EntradaHistorial>> HistorialZonaAsync(string codigo, TipoZona tipo, Guid? desastre, CancellationToken ct)
        {
            LecturasHistorial++;
            IReadOnlyList<EntradaHistorial> filas = Historial
                .Where(e => tipo == TipoZona.MUNICIPIO ? e.CodigoMunicipio == codigo : e.CodigoMunicipio.StartsWith(codigo, StringComparison.Ordinal))
                .Where(e => desastre is null || e.IdDesastre == desastre)
                .OrderBy(e => e.Instante)
                .ToList();
            return Task.FromResult(filas);
        }

        public Task<IReadOnlyList<ZonaMapa>> ZonasMapaAsync(FiltroMapa filtro, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<PuntoMapa>> PuntosAsync(FiltroMapa filtro, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, string>> GeometriasAsync(IReadOnlyCollection<string> codigos, CancellationToken ct) => throw new NotImplementedException();
        public Task<DateTimeOffset?> DatoAlAsync(Guid desastre, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ZonaConsolidada>> ConsolidadoAsync(Guid desastre, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<EvolucionActivo?> EvolucionAsync(Guid idActivo, CancellationToken ct) => throw new NotImplementedException();
        public Task<BandejaLeida> BandejaAsync(CancellationToken ct) => throw new NotImplementedException();
    }
}
