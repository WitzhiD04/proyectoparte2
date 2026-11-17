using Reconstruccion.Dominio;
using Xunit;

namespace Reconstruccion.Pruebas.Dominio;

public class ProyectoPruebas
{
    [Fact]
    public void Un_proyecto_nace_formulado_sin_presupuesto_ni_responsable_y_con_su_entrada_de_historial()
    {
        ProyectoReconstruccion p = Datos.ProyectoFormulado();

        Assert.Equal(EstadoProyecto.Formulado, p.Estado);
        Assert.Null(p.Presupuesto);
        Assert.Null(p.Responsable);
        Assert.Equal(1, p.Version);
        Assert.Single(p.EntradasNuevas);
        Assert.Equal(TipoCambio.Creacion, p.EntradasNuevas[0].TipoCambio);
    }

    [Fact]
    public void Solo_la_autoridad_crea_proyectos()
    {
        EvaluacionOrigen origen = new(Guid.NewGuid(), Guid.NewGuid(), "Puente", NivelDano.Grave, "63001");
        Assert.Throws<NoAutorizadoException>(() => ProyectoReconstruccion.Crear(Guid.NewGuid(), Datos.IdDesastre, "x",
            AlcanceTerritorial.Municipal, new[] { origen }, Datos.Responsable, Datos.Ahora));
    }

    [Fact]
    public void No_se_aprueba_un_proyecto_con_datos_incompletos()
    {
        ProyectoReconstruccion p = Datos.ProyectoFormulado();
        ReglaDeNegocioException e = Assert.Throws<ReglaDeNegocioException>(() => p.Aprobar(Datos.AutoridadLocal, Datos.Ahora));
        Assert.Equal("DATOS_INCOMPLETOS", e.Codigo);
        Assert.Equal(EstadoProyecto.Formulado, p.Estado);
    }

    [Fact]
    public void Un_proyecto_municipal_lo_aprueba_la_autoridad_local()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Municipal);
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadRegional, Datos.Ahora));
        p.Aprobar(Datos.AutoridadLocal, Datos.Ahora);
        Assert.Equal(EstadoProyecto.Aprobado, p.Estado);
    }

    [Fact]
    public void Un_proyecto_que_abarca_varios_municipios_lo_aprueba_la_autoridad_regional()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Regional);
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadLocal, Datos.Ahora));
        p.Aprobar(Datos.AutoridadRegional, Datos.Ahora);
        Assert.Equal(EstadoProyecto.Aprobado, p.Estado);
    }

    // ------------------------------------------------------------------ DA-14: jurisdicción por territorio

    [Fact]
    public void La_autoridad_local_de_otro_municipio_no_aprueba_un_proyecto_municipal()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Municipal);   // Armenia
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadLocalCalarca, Datos.Ahora));
        Assert.Equal(EstadoProyecto.Formulado, p.Estado);
    }

    [Fact]
    public void Un_proyecto_municipal_no_lo_aprueban_la_regional_de_su_departamento_ni_la_nacional()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Municipal);
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadRegional, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadNacional, Datos.Ahora));
        Assert.Equal(EstadoProyecto.Formulado, p.Estado);
    }

    [Fact]
    public void Una_autoridad_local_sin_territorio_no_aprueba()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Municipal);
        Actor sinTerritorio = Datos.AutoridadLocal with { Territorio = null };
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(sinTerritorio, Datos.Ahora));
    }

    [Fact]
    public void La_regional_de_otro_departamento_no_aprueba_un_proyecto_regional()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Regional);    // Armenia y Calarcá
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadRegionalRisaralda, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadNacional, Datos.Ahora));
        p.Aprobar(Datos.AutoridadRegional, Datos.Ahora);
        Assert.Equal(EstadoProyecto.Aprobado, p.Estado);
    }

    [Fact]
    public void Un_proyecto_de_varios_departamentos_lo_aprueba_solo_la_autoridad_nacional()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar(AlcanceTerritorial.Nacional);    // Quindío y Risaralda
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadLocal, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadRegional, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.Aprobar(Datos.AutoridadRegionalRisaralda, Datos.Ahora));
        p.Aprobar(Datos.AutoridadNacional, Datos.Ahora);
        Assert.Equal(EstadoProyecto.Aprobado, p.Estado);
    }

    [Theory]
    [InlineData(new[] { "63001" }, AlcanceTerritorial.Municipal)]
    [InlineData(new[] { "63001", "63001" }, AlcanceTerritorial.Municipal)]
    [InlineData(new[] { "63001", "63130" }, AlcanceTerritorial.Regional)]
    [InlineData(new[] { "63001", "66400" }, AlcanceTerritorial.Nacional)]
    public void El_alcance_sale_de_los_municipios_y_sus_departamentos(string[] municipios, AlcanceTerritorial esperado)
    {
        Assert.Equal(esperado, Jurisdiccion.AlcanceDe(municipios));
    }

    [Theory]
    [InlineData("alcaldia.armenia", new[] { "63001" }, true)]
    [InlineData("alcaldia.armenia", new[] { "63130" }, false)]
    [InlineData("alcaldia.armenia", new[] { "63001", "66400" }, true)]   // toca su municipio
    [InlineData("gobernacion.quindio", new[] { "63130" }, true)]
    [InlineData("gobernacion.quindio", new[] { "66400" }, false)]
    [InlineData("ungrd.operador", new[] { "66400" }, true)]
    [InlineData("contraloria", new[] { "66400" }, true)]                  // el auditor no tiene restricción territorial
    public void Cada_autoridad_ve_los_proyectos_de_su_jurisdiccion(string usuario, string[] municipios, bool ve)
    {
        Actor actor = new[] { Datos.AutoridadLocal, Datos.AutoridadRegional, Datos.AutoridadNacional, Datos.Auditor }
            .Single(a => a.Usuario == usuario);
        Assert.Equal(ve, Jurisdiccion.Cubre(actor, municipios));
    }

    [Fact]
    public void Sin_municipios_conocidos_solo_la_autoridad_nacional_y_el_auditor_ven_el_proyecto()
    {
        Assert.False(Jurisdiccion.Cubre(Datos.AutoridadLocal, null));
        Assert.False(Jurisdiccion.Cubre(Datos.AutoridadRegional, Array.Empty<string>()));
        Assert.True(Jurisdiccion.Cubre(Datos.AutoridadNacional, null));
        Assert.True(Jurisdiccion.Cubre(Datos.Auditor, null));
    }

    [Fact]
    public void Asignar_exige_justificacion_y_guarda_valor_anterior_y_nuevo()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar();
        p.LimpiarPendientes();

        Assert.Throws<ReglaDeNegocioException>(() => p.AsignarPresupuesto(Datos.AutoridadLocal, new Dinero(1), null, null, " ", Datos.Ahora));

        p.AsignarPresupuesto(Datos.AutoridadLocal, new Dinero(650_000_000), null, null, "Ajuste por diseños", Datos.Ahora);
        EntradaHistorial entrada = Assert.Single(p.EntradasNuevas);
        Assert.Equal(TipoCambio.Presupuesto, entrada.TipoCambio);
        Assert.Equal("500000000", entrada.ValorAnterior);
        Assert.Equal("650000000", entrada.ValorNuevo);
        Assert.Equal("Ajuste por diseños", entrada.Justificacion);
    }

    [Fact]
    public void Despues_de_aprobado_no_se_puede_dejar_el_presupuesto_en_cero()
    {
        ProyectoReconstruccion p = Datos.ProyectoAprobado();
        ReglaDeNegocioException e = Assert.Throws<ReglaDeNegocioException>(() =>
            p.AsignarPresupuesto(Datos.AutoridadLocal, Dinero.Cero, null, null, "Recorte", Datos.Ahora));
        Assert.Equal("DATOS_INCOMPLETOS", e.Codigo);
        Assert.Equal(500_000_000m, p.Presupuesto!.Value.Monto);
    }

    [Fact]
    public void El_cronograma_debe_terminar_despues_de_empezar()
    {
        Assert.Throws<ReglaDeNegocioException>(() => new Cronograma(new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 1)));
    }

    [Fact]
    public void Para_iniciar_la_ejecucion_se_necesita_contratista_y_ser_el_responsable_asignado()
    {
        ProyectoReconstruccion p = Datos.ProyectoAprobado();

        Assert.Throws<ReglaDeNegocioException>(() => p.IniciarEjecucion(Datos.Responsable, null, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.IniciarEjecucion(Datos.OtroResponsable, "Constructora Andina", Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.IniciarEjecucion(Datos.AutoridadLocal, "Constructora Andina", Datos.Ahora));

        p.IniciarEjecucion(Datos.Responsable, "Constructora Andina", Datos.Ahora);
        Assert.Equal(EstadoProyecto.EnEjecucion, p.Estado);
        Assert.Equal("Constructora Andina", p.Contratista);
    }

    [Fact]
    public void El_avance_y_los_costos_solo_se_registran_en_ejecucion()
    {
        ProyectoReconstruccion p = Datos.ProyectoAprobado();
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarAvance(Datos.Responsable, 10, null, Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarCosto(Datos.Responsable, new Dinero(1000), "Cemento", Datos.Ahora));
    }

    [Fact]
    public void El_avance_de_SE7_guarda_su_procedencia_y_uno_menor_queda_como_correccion_E04()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        p.LimpiarPendientes();

        p.RegistrarAvanceExterno("SE-7", 60, Datos.Ahora);
        p.RegistrarAvanceExterno("SE-7", 55, Datos.Ahora);
        p.RegistrarAvanceExterno("SE-7", 55, Datos.Ahora); // repetido: no cambia nada

        Assert.Equal(55m, p.Avance);
        Assert.Equal(["Avance reportado por SE-7", "Corrección reportada por SE-7"], p.EntradasNuevas.Select(e => e.Justificacion));
        Assert.All(p.EntradasNuevas, e => { Assert.Equal("SE-7", e.Procedencia); Assert.Equal("SE-7", e.Autor); });
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarAvanceExterno("SE-7", 101, Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => Datos.ProyectoAprobado().RegistrarAvanceExterno("SE-7", 10, Datos.Ahora));
    }

    [Fact]
    public void El_contratista_de_SE7_se_registra_salvo_en_un_proyecto_en_estado_final()
    {
        ProyectoReconstruccion p = Datos.ProyectoAprobado();
        p.RegistrarContratistaExterno("SE-7", "Obras del Quindío", Datos.Ahora);
        Assert.Equal("Obras del Quindío", p.Contratista);
        Assert.Equal("SE-7", p.EntradasNuevas[^1].Procedencia);

        ProyectoReconstruccion cancelado = Datos.ProyectoAprobado();
        cancelado.Cancelar(Datos.AutoridadLocal, "Sin recursos", Datos.Ahora);
        Assert.Throws<ReglaDeNegocioException>(() => cancelado.RegistrarContratistaExterno("SE-7", "Otro", Datos.Ahora));
    }

    [Fact]
    public void El_avance_va_de_0_a_100_y_bajar_exige_justificacion()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();

        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarAvance(Datos.Responsable, 101, null, Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarAvance(Datos.Responsable, -1, null, Datos.Ahora));

        p.RegistrarAvance(Datos.Responsable, 40, null, Datos.Ahora);
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarAvance(Datos.Responsable, 30, null, Datos.Ahora));
        p.RegistrarAvance(Datos.Responsable, 30, "Se había digitado mal", Datos.Ahora);
        Assert.Equal(30m, p.Avance);
    }

    [Fact]
    public void Un_costo_debe_ser_mayor_que_cero_y_tener_concepto()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarCosto(Datos.Responsable, Dinero.Cero, "Cemento", Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarCosto(Datos.Responsable, new Dinero(100), " ", Datos.Ahora));
    }

    [Fact]
    public void Superar_el_presupuesto_marca_sobrecosto_pero_no_bloquea_el_registro()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        p.RegistrarCosto(Datos.Responsable, new Dinero(400_000_000), "Estructura", Datos.Ahora);
        Assert.False(p.EnSobrecosto);

        p.RegistrarCosto(Datos.Responsable, new Dinero(150_000_000), "Imprevistos", Datos.Ahora);
        Assert.True(p.EnSobrecosto);
        Assert.Equal(550_000_000m, p.CostoEjecutado.Monto);
    }

    [Fact]
    public void Solo_se_finaliza_con_avance_de_100()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        p.RegistrarAvance(Datos.Responsable, 99, null, Datos.Ahora);
        Assert.Throws<ReglaDeNegocioException>(() => p.Finalizar(Datos.Responsable, Datos.Ahora));

        p.RegistrarAvance(Datos.Responsable, 100, null, Datos.Ahora);
        p.Finalizar(Datos.Responsable, Datos.Ahora);
        Assert.Equal(EstadoProyecto.Finalizado, p.Estado);
    }

    [Fact]
    public void Cancelar_exige_justificacion_y_libera_las_evaluaciones_sin_borrar_el_vinculo()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        Assert.Throws<ReglaDeNegocioException>(() => p.Cancelar(Datos.AutoridadLocal, "", Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => p.Cancelar(Datos.Responsable, "No procede", Datos.Ahora));

        p.Cancelar(Datos.AutoridadLocal, "El activo se reubicó", Datos.Ahora);
        Assert.Equal(EstadoProyecto.Cancelado, p.Estado);
        EvaluacionOrigen origen = Assert.Single(p.Evaluaciones);
        Assert.False(origen.Vigente);
    }

    [Fact]
    public void Un_estado_final_no_admite_ningun_cambio()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        p.Cancelar(Datos.AutoridadLocal, "El activo se reubicó", Datos.Ahora);
        int version = p.Version;

        Assert.Throws<ReglaDeNegocioException>(() => p.AsignarPresupuesto(Datos.AutoridadLocal, new Dinero(1), null, null, "x", Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.AsignarResponsable(Datos.AutoridadLocal, "otro", "x", Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarAvance(Datos.Responsable, 50, null, Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.RegistrarCosto(Datos.Responsable, new Dinero(1), "x", Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.AdjuntarDocumento(Datos.Responsable, "acta.pdf", "application/pdf", "u", "h", Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => p.Cancelar(Datos.AutoridadLocal, "otra vez", Datos.Ahora));
        Assert.Equal(version, p.Version);
    }

    [Fact]
    public void Una_accion_rechazada_no_deja_entrada_de_historial()
    {
        ProyectoReconstruccion p = Datos.ProyectoAprobado();
        p.LimpiarPendientes();
        Assert.Throws<ReglaDeNegocioException>(() => p.Finalizar(Datos.Responsable, Datos.Ahora));
        Assert.Empty(p.EntradasNuevas);
    }

    [Fact]
    public void El_numero_de_versiones_coincide_con_el_numero_de_entradas()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        p.RegistrarAvance(Datos.Responsable, 20, null, Datos.Ahora);
        p.RegistrarCosto(Datos.Responsable, new Dinero(10_000_000), "Demolición", Datos.Ahora);

        Assert.Equal(p.Version, p.EntradasNuevas.Count);
        Assert.Equal(Enumerable.Range(1, p.Version), p.EntradasNuevas.Select(e => e.Version));
    }

    [Fact]
    public void Un_proyecto_guardado_y_vuelto_a_cargar_queda_igual()
    {
        ProyectoReconstruccion original = Datos.ProyectoEnEjecucion();
        original.RegistrarAvance(Datos.Responsable, 35, null, Datos.Ahora);

        ProyectoReconstruccion cargado = ProyectoReconstruccion.Rehidratar(original.AInstantanea());

        Assert.Equal(original.Estado, cargado.Estado);
        Assert.Equal(original.Presupuesto, cargado.Presupuesto);
        Assert.Equal(original.Cronograma, cargado.Cronograma);
        Assert.Equal(original.Avance, cargado.Avance);
        Assert.Equal(original.Version, cargado.Version);
        Assert.Empty(cargado.EntradasNuevas);
    }
}

public class MaquinaEstadosPruebas
{
    [Fact]
    public void Solo_existen_las_transiciones_del_diagrama()
    {
        int permitidas = 0;
        foreach (EstadoProyecto estado in Enum.GetValues<EstadoProyecto>())
        {
            foreach (AccionProyecto accion in Enum.GetValues<AccionProyecto>())
            {
                if (MaquinaEstadosProyecto.PuedeEjecutar(estado, accion)) permitidas++;
            }
        }
        Assert.Equal(11, permitidas);
        Assert.False(MaquinaEstadosProyecto.PuedeEjecutar(EstadoProyecto.Formulado, AccionProyecto.Iniciar));
        Assert.False(MaquinaEstadosProyecto.PuedeEjecutar(EstadoProyecto.Aprobado, AccionProyecto.Finalizar));
        Assert.Equal(EstadoProyecto.Cancelado, MaquinaEstadosProyecto.Destino(EstadoProyecto.Aprobado, AccionProyecto.Cancelar));
    }

    [Fact]
    public void Finalizado_y_cancelado_no_tienen_salidas()
    {
        foreach (AccionProyecto accion in Enum.GetValues<AccionProyecto>())
        {
            Assert.False(MaquinaEstadosProyecto.PuedeEjecutar(EstadoProyecto.Finalizado, accion));
            Assert.False(MaquinaEstadosProyecto.PuedeEjecutar(EstadoProyecto.Cancelado, accion));
        }
    }
}

public class ServiciosPruebas
{
    [Fact]
    public void Son_candidatas_las_validadas_con_dano_grave_o_destruccion_total_sin_proyecto_vigente()
    {
        EvaluacionValidada grave = Datos.Evaluacion(nivel: NivelDano.Grave);
        EvaluacionValidada total = Datos.Evaluacion(nivel: NivelDano.DestruccionTotal);
        EvaluacionValidada moderado = Datos.Evaluacion(nivel: NivelDano.Moderado);
        EvaluacionValidada sinValidar = Datos.Evaluacion(estado: "REGISTRADA");
        EvaluacionValidada vinculada = Datos.Evaluacion();

        List<EvaluacionValidada> candidatas = CreacionDeProyectos.Candidatas(
            new[] { grave, total, moderado, sinValidar, vinculada }, new HashSet<Guid> { vinculada.Id });

        Assert.Equal(new[] { grave.Id, total.Id }, candidatas.Select(c => c.Id));
    }

    [Fact]
    public void Se_crea_un_proyecto_por_activo_con_todas_sus_evaluaciones()
    {
        Guid escuela = Guid.NewGuid();
        EvaluacionValidada a = Datos.Evaluacion(escuela);
        EvaluacionValidada b = Datos.Evaluacion(escuela);
        EvaluacionValidada puente = Datos.Evaluacion();

        List<ProyectoReconstruccion> proyectos = CreacionDeProyectos.CrearProyectos(Datos.IdDesastre, new[] { a, b, puente }, Datos.AutoridadLocal, Datos.Ahora);

        Assert.Equal(2, proyectos.Count);
        Assert.Equal(2, proyectos.Single(p => p.IdActivo == escuela).Evaluaciones.Count);
    }

    [Fact]
    public void Un_activo_evaluado_en_varios_municipios_da_un_proyecto_regional()
    {
        Guid via = Guid.NewGuid();
        List<ProyectoReconstruccion> proyectos = CreacionDeProyectos.CrearProyectos(Datos.IdDesastre,
            new[] { Datos.Evaluacion(via, municipio: "Armenia"), Datos.Evaluacion(via, municipio: "Calarcá") }, Datos.AutoridadRegional, Datos.Ahora);
        Assert.Equal(AlcanceTerritorial.Regional, Assert.Single(proyectos).Alcance);
    }

    // ------------------------------------------------------------------ jurisdicción al crear y en las candidatas (Cuadro 3 del SRS)

    [Fact]
    public void Calarca_no_crea_proyectos_con_evaluaciones_de_Armenia()
    {
        EvaluacionValidada armenia = Datos.Evaluacion(municipio: "Armenia");
        NoAutorizadoException e = Assert.Throws<NoAutorizadoException>(() =>
            CreacionDeProyectos.CrearProyectos(Datos.IdDesastre, new[] { armenia }, Datos.AutoridadLocalCalarca, Datos.Ahora));
        Assert.Contains("63001", e.Message);
    }

    [Fact]
    public void Un_lote_con_una_sola_evaluacion_fuera_de_la_jurisdiccion_no_crea_ninguno()
    {
        EvaluacionValidada armenia = Datos.Evaluacion(municipio: "Armenia");
        EvaluacionValidada calarca = Datos.Evaluacion(municipio: "Calarcá");
        Assert.Throws<NoAutorizadoException>(() =>
            CreacionDeProyectos.CrearProyectos(Datos.IdDesastre, new[] { armenia, calarca }, Datos.AutoridadLocal, Datos.Ahora));
    }

    [Fact]
    public void La_regional_del_Quindio_crea_con_evaluaciones_de_Armenia_y_Calarca_pero_no_de_Risaralda()
    {
        EvaluacionValidada armenia = Datos.Evaluacion(municipio: "Armenia");
        EvaluacionValidada calarca = Datos.Evaluacion(municipio: "Calarcá");
        Assert.Equal(2, CreacionDeProyectos.CrearProyectos(Datos.IdDesastre, new[] { armenia, calarca }, Datos.AutoridadRegional, Datos.Ahora).Count);
        Assert.Throws<NoAutorizadoException>(() => CreacionDeProyectos.CrearProyectos(Datos.IdDesastre,
            new[] { Datos.Evaluacion(municipio: "La Virginia") }, Datos.AutoridadRegional, Datos.Ahora));
    }

    [Fact]
    public void La_nacional_crea_con_evaluaciones_de_cualquier_departamento()
    {
        List<ProyectoReconstruccion> proyectos = CreacionDeProyectos.CrearProyectos(Datos.IdDesastre,
            new[] { Datos.Evaluacion(municipio: "Armenia"), Datos.Evaluacion(municipio: "La Virginia") }, Datos.AutoridadNacional, Datos.Ahora);
        Assert.Equal(2, proyectos.Count);
    }

    [Theory]
    [InlineData("alcaldia.calarca", new[] { "Calarcá" })]
    [InlineData("alcaldia.armenia", new[] { "Armenia" })]
    [InlineData("gobernacion.quindio", new[] { "Armenia", "Calarcá" })]
    [InlineData("ungrd.operador", new[] { "Armenia", "Calarcá", "La Virginia" })]
    public void Cada_autoridad_ve_las_candidatas_de_su_jurisdiccion(string usuario, string[] municipiosVisibles)
    {
        Actor actor = new[] { Datos.AutoridadLocal, Datos.AutoridadLocalCalarca, Datos.AutoridadRegional, Datos.AutoridadNacional }
            .Single(a => a.Usuario == usuario);
        EvaluacionValidada[] evaluaciones =
            [Datos.Evaluacion(municipio: "Armenia"), Datos.Evaluacion(municipio: "Calarcá"), Datos.Evaluacion(municipio: "La Virginia")];

        List<EvaluacionValidada> visibles = CreacionDeProyectos.Candidatas(evaluaciones, new HashSet<Guid>(), actor);

        Assert.Equal(municipiosVisibles.OrderBy(m => m), visibles.Select(e => e.Municipio).OrderBy(m => m));
    }

    [Fact]
    public void Un_activo_evaluado_en_varios_departamentos_da_un_proyecto_nacional()
    {
        Guid via = Guid.NewGuid();
        List<ProyectoReconstruccion> proyectos = CreacionDeProyectos.CrearProyectos(Datos.IdDesastre,
            new[] { Datos.Evaluacion(via, municipio: "Armenia"), Datos.Evaluacion(via, municipio: "La Virginia") }, Datos.AutoridadNacional, Datos.Ahora);
        Assert.Equal(AlcanceTerritorial.Nacional, Assert.Single(proyectos).Alcance);
    }

    [Fact]
    public void El_estado_en_una_fecha_pasada_sale_del_historial()
    {
        ProyectoReconstruccion p = Datos.ProyectoFormulado();
        DateTimeOffset dia1 = Datos.Ahora.AddDays(1), dia2 = Datos.Ahora.AddDays(2), dia3 = Datos.Ahora.AddDays(3);
        p.AsignarPresupuesto(Datos.AutoridadLocal, new Dinero(500), "Fondo", new Cronograma(new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 1)), "Inicial", dia1);
        p.AsignarResponsable(Datos.AutoridadLocal, Datos.Responsable.Usuario, "Inicial", dia1);
        p.Aprobar(Datos.AutoridadLocal, dia2);
        p.AsignarPresupuesto(Datos.AutoridadLocal, new Dinero(800), null, null, "Adición", dia3);

        EstadoEnFecha enDia1 = ReconstruccionHistorica.Reconstruir(p.EntradasNuevas.Where(e => e.Instante <= dia1))!;
        EstadoEnFecha hoy = ReconstruccionHistorica.Reconstruir(p.EntradasNuevas)!;

        Assert.Equal("Formulado", enDia1.Estado);
        Assert.Equal(500m, enDia1.Presupuesto);
        Assert.Equal("Aprobado", hoy.Estado);
        Assert.Equal(800m, hoy.Presupuesto);
        Assert.Equal(p.Version, hoy.Version);
        Assert.Null(ReconstruccionHistorica.Reconstruir(p.EntradasNuevas.Where(e => e.Instante < Datos.Ahora)));
    }

    [Fact]
    public void El_consolidado_pondera_el_avance_por_presupuesto_y_no_cuenta_los_cancelados()
    {
        List<FilaPrograma> filas = new()
        {
            new(Guid.NewGuid(), Datos.IdDesastre, "A", EstadoProyecto.EnEjecucion, 300, 100, 50, 5, Datos.Ahora),
            new(Guid.NewGuid(), Datos.IdDesastre, "B", EstadoProyecto.Finalizado, 100, 120, 100, 9, Datos.Ahora),
            new(Guid.NewGuid(), Datos.IdDesastre, "C", EstadoProyecto.Cancelado, 600, 10, 10, 4, Datos.Ahora),
        };

        Consolidado c = ConsolidadoPrograma.Calcular(Datos.IdDesastre, filas);

        Assert.Equal(3, c.TotalProyectos);
        Assert.Equal(1, c.ProyectosPorEstado[EstadoProyecto.EnEjecucion]);
        Assert.Equal(1000m, c.PresupuestoAsignado);
        Assert.Equal(230m, c.CostoEjecutado);
        Assert.Equal(62.5m, c.AvanceAgregado);          // (50*300 + 100*100) / 400
        Assert.Equal(filas[1].IdProyecto, Assert.Single(c.ProyectosEnSobrecosto));
        Assert.False(c.TodosEnEstadoFinal);
    }

    [Fact]
    public void El_desastre_puede_cerrarse_cuando_todos_los_proyectos_estan_en_estado_final()
    {
        List<FilaPrograma> filas = new()
        {
            new(Guid.NewGuid(), Datos.IdDesastre, "A", EstadoProyecto.Finalizado, 300, 100, 100, 5, Datos.Ahora),
            new(Guid.NewGuid(), Datos.IdDesastre, "B", EstadoProyecto.Cancelado, 100, 0, 0, 3, Datos.Ahora),
        };
        Assert.True(ConsolidadoPrograma.Calcular(Datos.IdDesastre, filas).TodosEnEstadoFinal);
    }
}
