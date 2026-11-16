using ConsultaDanos.Dominio;
using static ConsultaDanos.Pruebas.Hechos;

namespace ConsultaDanos.Pruebas;

public class PruebasIndice
{
    [Fact]
    public void El_indice_es_el_promedio_de_los_pesos_ponderado_por_la_cantidad_DA08()
    {
        // 2 sin daño (0), 1 moderado (0,5) y 1 destrucción total (1): (0 + 0 + 0,5 + 1) / 4 = 0,375.
        Distribucion d = new Distribucion().Sumar(NivelDano.SIN_DANO, 2).Sumar(NivelDano.MODERADO, 1).Sumar(NivelDano.DESTRUCCION_TOTAL, 1);
        Assert.Equal(0.375, d.Indice);
        Assert.Equal(4, d.Total);
        Assert.Equal(1, d.Graves);
    }

    [Theory]
    [InlineData(NivelDano.SIN_DANO, 0)]
    [InlineData(NivelDano.LEVE, 0.25)]
    [InlineData(NivelDano.MODERADO, 0.5)]
    [InlineData(NivelDano.GRAVE, 0.75)]
    [InlineData(NivelDano.DESTRUCCION_TOTAL, 1)]
    public void Los_pesos_son_los_del_mapa_de_calor(NivelDano nivel, double peso) => Assert.Equal(peso, IndiceDano.Peso(nivel));

    [Fact]
    public void Una_zona_sin_activos_no_tiene_indice() => Assert.Null(new Distribucion().Indice);
}

public class PruebasPriorizacion
{
    private static ZonaConsolidada Zona(string codigo, params (string Tipo, NivelDano Nivel, int Cantidad)[] conteos) =>
        new(codigo, codigo, codigo[..2], conteos.Select(c => new ConteoTipo(c.Tipo, null, c.Nivel, c.Cantidad)).ToList());

    [Fact]
    public void Primero_los_activos_criticos_graves_y_despues_el_indice_RFED10()
    {
        ZonaConsolidada muchasViviendasDestruidas = Zona("63001", ("VIVIENDA", NivelDano.DESTRUCCION_TOTAL, 40));
        ZonaConsolidada unHospitalGrave = Zona("63130", ("HOSPITAL", NivelDano.GRAVE, 1), ("VIVIENDA", NivelDano.LEVE, 10));
        ZonaConsolidada dosPuentesCaidos = Zona("63190", ("PUENTE", NivelDano.DESTRUCCION_TOTAL, 2));
        ZonaConsolidada escuelaModerada = Zona("63212", ("ESCUELA", NivelDano.MODERADO, 3));

        IReadOnlyList<ZonaPriorizada> orden = Priorizacion.Ordenar([muchasViviendasDestruidas, unHospitalGrave, dosPuentesCaidos, escuelaModerada],
            Priorizacion.TiposCriticosPorOmision);

        Assert.Equal(["63190", "63130", "63001", "63212"], orden.Select(z => z.Zona.CodigoMunicipio));
        Assert.Equal([1, 2, 3, 4], orden.Select(z => z.Prioridad));
        Assert.Equal(2, orden[0].CriticosGraves);
        // Sin críticos graves, decide el índice: 40 viviendas destruidas (1,0) van antes que una escuela moderada (0,5).
        Assert.Equal(1.0, orden[2].Indice);
    }

    [Fact]
    public void Un_empate_se_rompe_por_cantidad_y_codigo_para_que_el_orden_sea_estable()
    {
        ZonaConsolidada a = Zona("63401", ("VIVIENDA", NivelDano.GRAVE, 1));
        ZonaConsolidada b = Zona("63001", ("VIVIENDA", NivelDano.GRAVE, 1));
        ZonaConsolidada c = Zona("63130", ("VIVIENDA", NivelDano.GRAVE, 3));
        Assert.Equal(["63130", "63001", "63401"],
            Priorizacion.Ordenar([a, b, c], Priorizacion.TiposCriticosPorOmision).Select(z => z.Zona.CodigoMunicipio));
    }

    [Fact]
    public void Los_tipos_criticos_son_configurables()
    {
        ZonaConsolidada viviendas = Zona("63001", ("VIVIENDA", NivelDano.GRAVE, 1));
        ZonaConsolidada hospital = Zona("63130", ("HOSPITAL", NivelDano.GRAVE, 1));
        HashSet<string> soloVivienda = ["VIVIENDA"];
        Assert.Equal("63001", Priorizacion.Ordenar([hospital, viviendas], soloVivienda)[0].Zona.CodigoMunicipio);
    }
}

public class PruebasEstadoActivo
{
    private static readonly Guid Activo = Guid.NewGuid();
    private static readonly Guid Evaluacion = Guid.NewGuid();

    [Fact]
    public void Solo_una_version_mayor_del_activo_reemplaza_el_contenido()
    {
        EstadoActivo e = EstadoActivo.Vacio(Activo)
            .ConContenido(Contenido(Evaluacion, NivelDano.GRAVE, 3), 3, OrigenContenido.Evaluacion, _ => null);
        EstadoActivo atrasado = e.ConContenido(Contenido(Evaluacion, NivelDano.LEVE, 1), 1, OrigenContenido.Evaluacion, _ => null);
        Assert.Same(e, atrasado);
        Assert.Equal(NivelDano.GRAVE, atrasado.Contenido!.Nivel);
    }

    [Fact]
    public void Con_la_misma_version_una_sincronizacion_en_conflicto_no_reemplaza_la_vigente_AD32()
    {
        Guid entrante = Guid.NewGuid();
        EstadoActivo vigente = EstadoActivo.Vacio(Activo)
            .ConContenido(Contenido(Evaluacion, NivelDano.LEVE, 1), 4, OrigenContenido.Evaluacion, _ => null);
        // La regla de conflicto no aplicó la entrante: EV-09 trae su contenido con la versión que ya tenía el activo.
        EstadoActivo despues = vigente.ConContenido(Contenido(entrante, NivelDano.DESTRUCCION_TOTAL, 1), 4,
            OrigenContenido.SincronizacionEnConflicto, _ => null);
        Assert.Equal(Evaluacion, despues.Contenido!.IdEvaluacion);

        // En el otro orden, la vigente que llega después la reemplaza: el resultado no depende del orden.
        EstadoActivo alReves = EstadoActivo.Vacio(Activo)
            .ConContenido(Contenido(entrante, NivelDano.DESTRUCCION_TOTAL, 1), 4, OrigenContenido.SincronizacionEnConflicto, _ => null)
            .ConContenido(Contenido(Evaluacion, NivelDano.LEVE, 1), 4, OrigenContenido.Evaluacion, _ => null);
        Assert.Equal(despues, alReves);
    }

    [Fact]
    public void Si_cambia_la_evaluacion_vigente_la_validacion_es_la_de_la_nueva()
    {
        Guid otra = Guid.NewGuid();
        EstadoActivo validada = EstadoActivo.Vacio(Activo)
            .ConContenido(Contenido(Evaluacion, NivelDano.GRAVE, 2), 1, OrigenContenido.Evaluacion, _ => EstadoValidacion.VALIDADA);
        Assert.True(validada.Validada);
        EstadoActivo otraVigente = validada.ConContenido(Contenido(otra, NivelDano.GRAVE, 1), 2, OrigenContenido.Resolucion, _ => null);
        Assert.False(otraVigente.Validada);
        Assert.Empty(Consolidacion.Deltas(otraVigente, otraVigente));
        Assert.Equal([(new ClaveZona(Desastre, "63001", "VIVIENDA", NivelDano.GRAVE), -1)], Consolidacion.Deltas(validada, otraVigente));
    }

    [Fact]
    public void Una_validacion_de_otra_evaluacion_no_cambia_el_activo()
    {
        EstadoActivo e = EstadoActivo.Vacio(Activo).ConContenido(Contenido(Evaluacion, NivelDano.GRAVE, 2), 1, OrigenContenido.Evaluacion, _ => null);
        Assert.Same(e, e.ConValidacion(Guid.NewGuid(), EstadoValidacion.VALIDADA));
    }

    [Fact]
    public void Cambiar_de_municipio_o_de_nivel_mueve_el_activo_de_grupo_en_el_consolidado()
    {
        EstadoActivo antes = EstadoActivo.Vacio(Activo)
            .ConContenido(Contenido(Evaluacion, NivelDano.LEVE, 2), 1, OrigenContenido.Evaluacion, _ => EstadoValidacion.VALIDADA);
        EstadoActivo despues = antes.ConContenido(Contenido(Evaluacion, NivelDano.GRAVE, 3, "63130"), 2, OrigenContenido.Evaluacion, _ => null);
        Assert.Equal(
            [(new ClaveZona(Desastre, "63001", "VIVIENDA", NivelDano.LEVE), -1), (new ClaveZona(Desastre, "63130", "VIVIENDA", NivelDano.GRAVE), +1)],
            Consolidacion.Deltas(antes, despues));
    }

    [Fact]
    public void Un_activo_fusionado_o_sin_desastre_no_cuenta()
    {
        EstadoActivo validada = EstadoActivo.Vacio(Activo)
            .ConContenido(Contenido(Evaluacion, NivelDano.GRAVE, 2), 1, OrigenContenido.Evaluacion, _ => EstadoValidacion.VALIDADA);
        Assert.Null(validada.FusionarEn(Guid.NewGuid()).ClaveConsolidado);
        EstadoActivo preliminar = EstadoActivo.Vacio(Activo).ConContenido(
            Contenido(Evaluacion, NivelDano.GRAVE, 2) with { IdDesastre = null }, 1, OrigenContenido.Evaluacion, _ => EstadoValidacion.VALIDADA);
        Assert.False(preliminar.Visible);
        Assert.Null(preliminar.ClaveConsolidado);
    }
}

public class PruebasIndicadores
{
    private static readonly DateTimeOffset Ahora = T0.AddHours(30);

    [Fact]
    public void La_alarma_se_activa_al_superar_el_tamano_o_la_antiguedad_R08()
    {
        UmbralesRevision u = new(50, 24);
        IndicadoresRevision justo = IndicadoresRevision.Calcular(50, Ahora.AddHours(-24), Ahora, u);
        Assert.False(justo.Alarma);
        Assert.Equal(24, justo.AntiguedadHoras);

        Assert.True(IndicadoresRevision.Calcular(51, Ahora.AddHours(-1), Ahora, u).SuperaTamano);
        IndicadoresRevision viejo = IndicadoresRevision.Calcular(3, Ahora.AddHours(-25), Ahora, u);
        Assert.True(viejo.SuperaAntiguedad);
        Assert.True(viejo.Alarma);
    }

    [Fact]
    public void Sin_conflictos_pendientes_no_hay_antiguedad()
    {
        IndicadoresRevision vacia = IndicadoresRevision.Calcular(0, null, Ahora, UmbralesRevision.PorOmision);
        Assert.Null(vacia.AntiguedadHoras);
        Assert.False(vacia.Alarma);
    }
}
