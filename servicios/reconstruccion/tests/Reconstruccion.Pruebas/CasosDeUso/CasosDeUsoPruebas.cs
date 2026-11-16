using System.Text;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Dominio;
using Xunit;

namespace Reconstruccion.Pruebas.CasosDeUso;

public class CrearProyectosPorLotePruebas
{
    private readonly Memoria _memoria = new();
    private readonly RelojFijo _reloj = new();
    private readonly EvaluacionValidada _escuela = Datos.Evaluacion();
    private readonly EvaluacionValidada _puente = Datos.Evaluacion();

    public CrearProyectosPorLotePruebas()
    {
        _memoria.Desastres.Add(new DesastreLocal(Datos.IdDesastre, "Sismo del Eje Cafetero", EstadosDesastre.EnReconstruccion));
        _memoria.Evaluaciones.Add(_escuela);
        _memoria.Evaluaciones.Add(_puente);
    }

    private CrearProyectosPorLote CasoDeUso() => new(_memoria, _memoria, _memoria.Guardado(), _reloj);

    private async Task<SalidaDePrueba<ResultadoLoteDto>> Crear(string clave, params Guid[] ids)
    {
        SalidaDePrueba<ResultadoLoteDto> salida = new();
        await CasoDeUso().EjecutarAsync(new SolicitudLote(Datos.AutoridadLocal, Datos.IdDesastre, ids, clave), salida);
        return salida;
    }

    [Fact]
    public async Task Crea_los_proyectos_con_su_historial_y_sus_eventos_en_una_sola_confirmacion()
    {
        SalidaDePrueba<ResultadoLoteDto> salida = await Crear("lote-1", _escuela.Id, _puente.Id);

        Assert.Equal("exito", salida.Resultado);
        Assert.Equal(2, salida.Respuesta!.IdsProyectos.Count);
        Assert.Equal(2, _memoria.Proyectos.Count);
        Assert.Equal(2, _memoria.Historial.Count);
        Assert.Equal(2, _memoria.Bandeja.Count);
        Assert.All(_memoria.Bandeja, m => Assert.Equal(TipoCambio.Creacion, m.Entrada.TipoCambio));
        Assert.Equal(1, _memoria.Confirmaciones);
    }

    [Fact]
    public async Task Si_una_evaluacion_falla_no_se_crea_ningun_proyecto_y_se_informa_cual()
    {
        EvaluacionValidada leve = Datos.Evaluacion(nivel: NivelDano.Leve);
        _memoria.Evaluaciones.Add(leve);

        SalidaDePrueba<ResultadoLoteDto> salida = await Crear("lote-2", _escuela.Id, leve.Id);

        Assert.Equal("regla", salida.Resultado);
        Assert.Equal("LOTE_INVALIDO", salida.Codigo);
        Assert.Contains(leve.Id.ToString(), Assert.Single(salida.Detalles));
        Assert.Empty(_memoria.Proyectos);
        Assert.Empty(_memoria.Historial);
        Assert.Empty(_memoria.Bandeja);
    }

    [Fact]
    public async Task Reenviar_el_mismo_lote_devuelve_el_resultado_original_sin_duplicar()
    {
        SalidaDePrueba<ResultadoLoteDto> primero = await Crear("lote-3", _escuela.Id);
        SalidaDePrueba<ResultadoLoteDto> segundo = await Crear("lote-3", _escuela.Id);

        Assert.False(primero.Respuesta!.Reenvio);
        Assert.True(segundo.Respuesta!.Reenvio);
        Assert.Equal(primero.Respuesta.IdsProyectos, segundo.Respuesta.IdsProyectos);
        Assert.Single(_memoria.Proyectos);
    }

    [Fact]
    public async Task Una_evaluacion_no_puede_estar_en_dos_proyectos_vigentes_pero_queda_libre_al_cancelar()
    {
        SalidaDePrueba<ResultadoLoteDto> primero = await Crear("lote-4", _escuela.Id);
        SalidaDePrueba<ResultadoLoteDto> repetido = await Crear("lote-5", _escuela.Id);
        Assert.Equal("regla", repetido.Resultado);

        SalidaDePrueba<ProyectoDto> cancelacion = new();
        await new CancelarProyecto(_memoria, _memoria.Guardado(), _reloj).EjecutarAsync(
            new SolicitudCancelar(Datos.AutoridadLocal, primero.Respuesta!.IdsProyectos[0], "Se rehará el alcance"), cancelacion);
        Assert.Equal("exito", cancelacion.Resultado);

        SalidaDePrueba<ResultadoLoteDto> despues = await Crear("lote-6", _escuela.Id);
        Assert.Equal("exito", despues.Resultado);
    }

    [Fact]
    public async Task No_se_crean_proyectos_si_el_desastre_no_esta_en_reconstruccion()
    {
        _memoria.Desastres[0] = new DesastreLocal(Datos.IdDesastre, "Sismo", "EN_RECUPERACION");
        SalidaDePrueba<ResultadoLoteDto> salida = await Crear("lote-7", _escuela.Id);
        Assert.Equal("regla", salida.Resultado);
        Assert.Empty(_memoria.Proyectos);
    }

    [Fact]
    public async Task Solo_la_autoridad_crea_y_consulta_candidatas()
    {
        SalidaDePrueba<ResultadoLoteDto> salida = new();
        await CasoDeUso().EjecutarAsync(new SolicitudLote(Datos.Responsable, Datos.IdDesastre, new[] { _escuela.Id }, "lote-8"), salida);
        Assert.Equal("no_autorizado", salida.Resultado);

        SalidaDePrueba<IReadOnlyList<CandidataDto>> candidatas = new();
        await new ConsultarCandidatas(_memoria, _memoria).EjecutarAsync(new SolicitudCandidatas(Datos.Auditor, Datos.IdDesastre), candidatas);
        Assert.Equal("no_autorizado", candidatas.Resultado);
    }

    [Fact]
    public async Task Las_candidatas_dejan_de_listar_las_evaluaciones_que_ya_tienen_proyecto()
    {
        await Crear("lote-9", _escuela.Id);
        SalidaDePrueba<IReadOnlyList<CandidataDto>> salida = new();
        await new ConsultarCandidatas(_memoria, _memoria).EjecutarAsync(new SolicitudCandidatas(Datos.AutoridadLocal, Datos.IdDesastre), salida);
        Assert.Equal(_puente.Id, Assert.Single(salida.Respuesta!).IdEvaluacion);
    }
}

public class OperacionesPruebas
{
    private readonly Memoria _memoria = new();
    private readonly RelojFijo _reloj = new();

    [Fact]
    public async Task Aprobar_guarda_el_cambio_su_entrada_y_su_evento_juntos()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar();
        _memoria.Sembrar(p);
        int entradasAntes = _memoria.Historial.Count;

        SalidaDePrueba<ProyectoDto> salida = new();
        await new AprobarProyecto(_memoria, _memoria.Guardado(), _reloj).EjecutarAsync(new SolicitudAprobar(Datos.AutoridadLocal, p.Id), salida);

        Assert.Equal("exito", salida.Resultado);
        Assert.Equal("Aprobado", salida.Respuesta!.Estado);
        Assert.Equal(EstadoProyecto.Aprobado, _memoria.Proyectos[p.Id].Estado);
        Assert.Equal(entradasAntes + 1, _memoria.Historial.Count);
        Assert.Equal(TipoCambio.Estado, Assert.Single(_memoria.Bandeja).Entrada.TipoCambio);
        Assert.Equal(_memoria.Proyectos[p.Id].Version, _memoria.Historial.Count(e => e.IdProyecto == p.Id));
    }

    [Fact]
    public async Task Una_regla_incumplida_no_guarda_nada()
    {
        ProyectoReconstruccion p = Datos.ProyectoFormulado();
        _memoria.Sembrar(p);

        SalidaDePrueba<ProyectoDto> salida = new();
        await new AprobarProyecto(_memoria, _memoria.Guardado(), _reloj).EjecutarAsync(new SolicitudAprobar(Datos.AutoridadLocal, p.Id), salida);

        Assert.Equal("regla", salida.Resultado);
        Assert.Equal("DATOS_INCOMPLETOS", salida.Codigo);
        Assert.Equal(0, _memoria.Confirmaciones);
        Assert.Empty(_memoria.Bandeja);
    }

    [Fact]
    public async Task Un_proyecto_que_no_existe_se_informa_como_no_encontrado()
    {
        SalidaDePrueba<ProyectoDto> salida = new();
        await new FinalizarProyecto(_memoria, _memoria.Guardado(), _reloj).EjecutarAsync(new SolicitudFinalizar(Datos.Responsable, Guid.NewGuid()), salida);
        Assert.Equal("no_encontrado", salida.Resultado);
    }

    [Fact]
    public async Task Registrar_avance_y_costo_actualiza_los_acumulados_y_marca_sobrecosto()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        _memoria.Sembrar(p);

        SalidaDePrueba<ProyectoDto> salida = new();
        await new RegistrarAvanceYCosto(_memoria, _memoria.Guardado(), _reloj).EjecutarAsync(
            new SolicitudAvanceYCosto(Datos.Responsable, p.Id, 60, 520_000_000, "Estructura y cubierta", null), salida);

        Assert.Equal("exito", salida.Resultado);
        Assert.Equal(60m, salida.Respuesta!.Avance);
        Assert.True(salida.Respuesta.EnSobrecosto);
        Assert.Equal(2, _memoria.Bandeja.Count);
    }

    [Fact]
    public async Task El_ciclo_completo_llega_a_finalizado_y_el_consolidado_lo_refleja()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        _memoria.Sembrar(p);
        GuardadoDeProyectos guardado = _memoria.Guardado();

        await new RegistrarAvanceYCosto(_memoria, guardado, _reloj).EjecutarAsync(
            new SolicitudAvanceYCosto(Datos.Responsable, p.Id, 100, 480_000_000, "Obra completa", null), new SalidaDePrueba<ProyectoDto>());
        SalidaDePrueba<ProyectoDto> fin = new();
        await new FinalizarProyecto(_memoria, guardado, _reloj).EjecutarAsync(new SolicitudFinalizar(Datos.Responsable, p.Id), fin);
        Assert.Equal("Finalizado", fin.Respuesta!.Estado);

        // El modelo de lectura se alimenta con los hechos publicados (el adaptador del bus los traduce igual); se
        // aplican dos veces para probar que repetir no daña.
        ActualizarProyeccionPrograma proyeccion = new(_memoria);
        foreach (HechoProyecto hecho in _memoria.Bandeja.Concat(_memoria.Bandeja))
        {
            ResumenDeProyecto r = hecho.Proyecto;
            await proyeccion.EjecutarAsync(new FilaPrograma(r.IdProyecto, r.IdDesastre, r.Nombre, r.Estado, r.Presupuesto ?? 0,
                r.CostoEjecutado, r.Avance, r.Version, hecho.Entrada.Instante));
        }
        SalidaDePrueba<ConsolidadoDto> consolidado = new();
        await new ConsultarConsolidado(_memoria).EjecutarAsync(new SolicitudConsolidado(Datos.Auditor, Datos.IdDesastre), consolidado);

        Assert.Equal(1, consolidado.Respuesta!.TotalProyectos);
        Assert.Equal(1, consolidado.Respuesta.ProyectosPorEstado["Finalizado"]);
        Assert.Equal(100m, consolidado.Respuesta.AvanceAgregado);
        Assert.Equal(480_000_000m, consolidado.Respuesta.CostoEjecutado);
        Assert.True(consolidado.Respuesta.TodosEnEstadoFinal);
        Assert.Equal(_reloj.Ahora, consolidado.Respuesta.DatoAl);
    }

    [Fact]
    public async Task El_historial_devuelve_el_proyecto_como_estaba_en_una_fecha()
    {
        ProyectoReconstruccion p = Datos.ProyectoListoParaAprobar();
        _memoria.Sembrar(p);
        _reloj.Ahora = Datos.Ahora.AddDays(5);
        await new AprobarProyecto(_memoria, _memoria.Guardado(), _reloj).EjecutarAsync(new SolicitudAprobar(Datos.AutoridadLocal, p.Id), new SalidaDePrueba<ProyectoDto>());

        SalidaDePrueba<HistorialDto> antes = new();
        SalidaDePrueba<HistorialDto> completo = new();
        ConsultarHistorial consulta = new(_memoria);
        await consulta.EjecutarAsync(new SolicitudHistorial(Datos.Auditor, p.Id, Datos.Ahora.AddDays(1)), antes);
        await consulta.EjecutarAsync(new SolicitudHistorial(Datos.Auditor, p.Id, null), completo);

        Assert.Equal("Formulado", antes.Respuesta!.EstadoEnFecha!.Estado);
        Assert.Equal(antes.Respuesta.Entradas.Count + 1, completo.Respuesta!.Entradas.Count);
        Assert.Equal("Aprobado", completo.Respuesta.Entradas[completo.Respuesta.Entradas.Count - 1].ValorNuevo);
    }

    [Fact]
    public async Task El_responsable_no_consulta_el_historial()
    {
        SalidaDePrueba<HistorialDto> salida = new();
        await new ConsultarHistorial(_memoria).EjecutarAsync(new SolicitudHistorial(Datos.Responsable, Guid.NewGuid(), null), salida);
        Assert.Equal("no_autorizado", salida.Resultado);
    }

    [Fact]
    public async Task Adjuntar_un_documento_guarda_el_archivo_con_su_huella_autor_y_fecha()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        _memoria.Sembrar(p);
        byte[] contenido = Encoding.UTF8.GetBytes("Acta de inicio de obra");

        SalidaDePrueba<DocumentoDto> salida = new();
        await new AdjuntarDocumento(_memoria, _memoria, _memoria.Guardado(), _reloj).EjecutarAsync(
            new SolicitudDocumento(Datos.Responsable, p.Id, "acta-inicio.pdf", "application/pdf", new MemoryStream(contenido)), salida);

        Assert.Equal("exito", salida.Resultado);
        Assert.Equal(64, salida.Respuesta!.Hash.Length);
        Assert.Equal(Datos.Responsable.Usuario, salida.Respuesta.Autor);
        Assert.Equal(contenido, _memoria.Archivos[salida.Respuesta.Ubicacion]);
    }

    [Fact]
    public async Task No_se_sube_el_archivo_si_el_proyecto_esta_en_estado_final()
    {
        ProyectoReconstruccion p = Datos.ProyectoEnEjecucion();
        p.Cancelar(Datos.AutoridadLocal, "Se reubicó", Datos.Ahora);
        _memoria.Sembrar(p);

        SalidaDePrueba<DocumentoDto> salida = new();
        await new AdjuntarDocumento(_memoria, _memoria, _memoria.Guardado(), _reloj).EjecutarAsync(
            new SolicitudDocumento(Datos.Responsable, p.Id, "acta.pdf", "application/pdf", new MemoryStream(new byte[] { 1, 2, 3 })), salida);

        Assert.Equal("regla", salida.Resultado);
        Assert.Empty(_memoria.Archivos);
    }
}
