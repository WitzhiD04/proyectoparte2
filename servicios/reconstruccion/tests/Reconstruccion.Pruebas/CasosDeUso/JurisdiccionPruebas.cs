using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Dominio;
using Xunit;

namespace Reconstruccion.Pruebas.CasosDeUso;

/// <summary>
/// Lecturas y creación con jurisdicción (Cuadro 3 del SRS, DA-14): la autoridad solo ve y usa lo de su territorio. Fuera
/// de él, un proyecto "no existe" (404) y un lote con evaluaciones ajenas se rechaza entero (403). El auditor ve todo.
/// </summary>
public class LecturasPorJurisdiccionPruebas
{
    private readonly Memoria _memoria = new();
    private readonly RelojFijo _reloj = new();
    private readonly ProyectoReconstruccion _deArmenia = Datos.ProyectoAprobado();       // municipio 63001
    private readonly EvaluacionValidada _evaluacionArmenia = Datos.Evaluacion(municipio: "Armenia");
    private readonly EvaluacionValidada _evaluacionCalarca = Datos.Evaluacion(municipio: "Calarcá");

    public LecturasPorJurisdiccionPruebas()
    {
        _memoria.Sembrar(_deArmenia);
        _memoria.Desastres.Add(new DesastreLocal(Datos.IdDesastre, "Sismo del Eje Cafetero", EstadosDesastre.EnReconstruccion));
        _memoria.Evaluaciones.Add(_evaluacionArmenia);
        _memoria.Evaluaciones.Add(_evaluacionCalarca);
    }

    /// <summary>Quién ve el proyecto de Armenia: Calarcá no; Armenia, la regional del Quindío, la nacional y el auditor sí.</summary>
    public static TheoryData<string, bool> QuienVeElProyectoDeArmenia => new()
    {
        { "alcaldia.calarca", false },
        { "alcaldia.armenia", true },
        { "gobernacion.quindio", true },
        { "ungrd.operador", true },
        { "contraloria", true },
    };

    private static Actor Actor(string usuario) =>
        new[] { Datos.AutoridadLocal, Datos.AutoridadLocalCalarca, Datos.AutoridadRegional, Datos.AutoridadNacional, Datos.Auditor }
            .Single(a => a.Usuario == usuario);

    private CrearProyectosPorLote CrearLote() => new(_memoria, _memoria, _memoria.Guardado(), _reloj);

    [Theory]
    [MemberData(nameof(QuienVeElProyectoDeArmenia))]
    public async Task La_lista_de_proyectos_solo_trae_los_de_su_jurisdiccion(string usuario, bool ve)
    {
        SalidaDePrueba<IReadOnlyList<ProyectoDto>> salida = new();
        await new ConsultarProyectos(_memoria).EjecutarAsync(new SolicitudProyectos(Actor(usuario), Datos.IdDesastre), salida);
        Assert.Equal("exito", salida.Resultado);
        Assert.Equal(ve ? 1 : 0, salida.Respuesta!.Count);
    }

    [Theory]
    [MemberData(nameof(QuienVeElProyectoDeArmenia))]
    public async Task El_detalle_fuera_de_la_jurisdiccion_responde_como_si_no_existiera(string usuario, bool ve)
    {
        SalidaDePrueba<ProyectoDto> salida = new();
        await new ConsultarProyecto(_memoria).EjecutarAsync(new SolicitudProyecto(Actor(usuario), _deArmenia.Id), salida);
        Assert.Equal(ve ? "exito" : "no_encontrado", salida.Resultado);
    }

    [Theory]
    [MemberData(nameof(QuienVeElProyectoDeArmenia))]
    public async Task El_historial_fuera_de_la_jurisdiccion_responde_como_si_no_existiera(string usuario, bool ve)
    {
        SalidaDePrueba<HistorialDto> salida = new();
        await new ConsultarHistorial(_memoria, _memoria).EjecutarAsync(new SolicitudHistorial(Actor(usuario), _deArmenia.Id, null), salida);
        Assert.Equal(ve ? "exito" : "no_encontrado", salida.Resultado);
    }

    [Fact]
    public async Task El_mensaje_es_el_mismo_que_el_de_un_proyecto_que_no_existe()
    {
        SalidaDePrueba<ProyectoDto> ajeno = new(), inexistente = new();
        await new ConsultarProyecto(_memoria).EjecutarAsync(new SolicitudProyecto(Datos.AutoridadLocalCalarca, _deArmenia.Id), ajeno);
        await new ConsultarProyecto(_memoria).EjecutarAsync(new SolicitudProyecto(Datos.AutoridadLocalCalarca, Guid.NewGuid()), inexistente);
        Assert.Equal(inexistente.Mensaje, ajeno.Mensaje);
    }

    [Theory]
    [InlineData("alcaldia.calarca", 1)]
    [InlineData("alcaldia.armenia", 1)]
    [InlineData("gobernacion.quindio", 2)]
    [InlineData("ungrd.operador", 2)]
    public async Task Las_candidatas_son_las_de_su_jurisdiccion(string usuario, int cuantas)
    {
        SalidaDePrueba<IReadOnlyList<CandidataDto>> salida = new();
        await new ConsultarCandidatas(_memoria, _memoria).EjecutarAsync(new SolicitudCandidatas(Actor(usuario), Datos.IdDesastre), salida);
        Assert.Equal("exito", salida.Resultado);
        Assert.Equal(cuantas, salida.Respuesta!.Count);
    }

    [Fact]
    public async Task Calarca_no_crea_con_una_evaluacion_de_Armenia_y_no_queda_nada_guardado()
    {
        int antes = _memoria.Proyectos.Count;
        SalidaDePrueba<ResultadoLoteDto> salida = new();
        await CrearLote().EjecutarAsync(new SolicitudLote(Datos.AutoridadLocalCalarca, Datos.IdDesastre,
            new[] { _evaluacionCalarca.Id, _evaluacionArmenia.Id }, "lote-ajeno"), salida);

        Assert.Equal("no_autorizado", salida.Resultado);
        Assert.Equal(antes, _memoria.Proyectos.Count);
        Assert.Empty(_memoria.Bandeja);
        Assert.Empty(_memoria.Lotes);
    }

    [Fact]
    public async Task La_jurisdiccion_se_revisa_antes_que_las_demas_reglas_del_lote()
    {
        // La evaluación de Armenia ya tiene proyecto: a Calarcá no se le dice eso (422), se le niega (403).
        await CrearLote().EjecutarAsync(new SolicitudLote(Datos.AutoridadLocal, Datos.IdDesastre, new[] { _evaluacionArmenia.Id }, "lote-1"),
            new SalidaDePrueba<ResultadoLoteDto>());
        SalidaDePrueba<ResultadoLoteDto> salida = new();
        await CrearLote().EjecutarAsync(new SolicitudLote(Datos.AutoridadLocalCalarca, Datos.IdDesastre, new[] { _evaluacionArmenia.Id }, "lote-2"), salida);
        Assert.Equal("no_autorizado", salida.Resultado);
    }

    [Fact]
    public async Task La_regional_del_Quindio_crea_el_lote_con_evaluaciones_de_Armenia_y_Calarca()
    {
        SalidaDePrueba<ResultadoLoteDto> salida = new();
        await CrearLote().EjecutarAsync(new SolicitudLote(Datos.AutoridadRegional, Datos.IdDesastre,
            new[] { _evaluacionCalarca.Id, _evaluacionArmenia.Id }, "lote-regional"), salida);

        Assert.Equal("exito", salida.Resultado);
        Assert.Equal(2, salida.Respuesta!.IdsProyectos.Count);
    }
}
