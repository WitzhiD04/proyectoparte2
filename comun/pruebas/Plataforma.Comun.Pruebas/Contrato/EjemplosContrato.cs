using System.Text.Json;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;

namespace Plataforma.Comun.Pruebas.Contrato;

/// <summary>
/// Un ejemplo válido de los datos de cada evento en alcance. Sirve también como documentación del contrato
/// para quien implemente un productor o un consumidor (por ejemplo, Evaluación de Daños en el paso 2).
/// </summary>
public static class EjemplosContrato
{
    private static readonly Guid Desastre = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Evaluacion = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Activo = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Proyecto = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Instante = new(2026, 11, 16, 14, 3, 22, TimeSpan.Zero);
    private static readonly Ubicacion Armenia = new(4.5339, -75.6811);

    private static JsonElement Poligono() => JsonSerializer.SerializeToElement(new
    {
        type = "Polygon",
        coordinates = new[] { new[] { new[] { -75.70, 4.52 }, new[] { -75.66, 4.52 }, new[] { -75.66, 4.56 }, new[] { -75.70, 4.52 } } },
    });

    private static readonly ResumenProyecto Resumen = new(Desastre, "Reconstrucción de Escuela La Esperanza",
        EstadoProyecto.EN_EJECUCION, 850_000_000m, 120_000_000m, 35m, 7);

    private static InstantaneaEvaluacion Instantanea() => new(Evaluacion, Activo, Desastre, "ESCUELA", NivelDano.GRAVE,
        Armenia, 4.5, "63001", EstadoEvaluacion.REGISTRADA, 3, 5);

    public static IReadOnlyDictionary<string, object> Datos { get; } = new Dictionary<string, object>
    {
        ["EV-03"] = new DesastreCreadoV1(Desastre, "SISMO", Severidad.NIVEL_3, 1, EstadoDesastre.REGISTRADO,
            "ungrd", "coordinador.quindio", OrigenDesastre.MANUAL, "Sismo del Eje Cafetero", "Sismo de magnitud 6,2",
            Instante, Armenia, 1),
        ["EV-04"] = new SeveridadCambiadaV1(Desastre, Severidad.NIVEL_3, Severidad.NIVEL_4, 2, "coordinador.quindio",
            "Nuevas evaluaciones en Calarcá"),
        ["EV-05"] = new ZonaActualizadaV1(Desastre, 2, Poligono(), ["63001", "63130"], "manual", "coordinador.quindio"),
        ["EV-06"] = new EstadoDesastreCambiadoV1(Desastre, EstadoDesastre.EN_RECUPERACION, EstadoDesastre.EN_RECONSTRUCCION,
            "coordinador.quindio", 4),
        ["EV-08"] = new EvaluacionValidadaV1(Evaluacion, Desastre, "ESCUELA", NivelDano.GRAVE, Armenia, "ana.perez",
            "defensa-civil", 3, Activo, "Escuela La Esperanza", "63001", "63", EstadoValidacion.VALIDADA,
            "alcaldia.armenia", Instante),
        ["EV-09"] = new EvaluacionSincronizadaV1(Evaluacion, "dispositivo-7", Instante.AddHours(-3), Instante, false, 1,
            Activo, Desastre, "ESCUELA", NivelDano.GRAVE, Armenia, 4.5, "63001", EstadoEvaluacion.REGISTRADA, 1, 1),
        ["EV-10"] = new ConflictoDetectadoV1(Guid.NewGuid(), [Evaluacion, Guid.NewGuid()], "AD-43: versión mayor que la base de otro dispositivo",
            [new VersionConservada(Evaluacion, 2, true)], EstadoRevision.PENDIENTE, Activo, TipoConflicto.VERSION),
        ["EV-18"] = new ProyectoCreadoV1(Proyecto, [Evaluacion], Desastre, null, null, null, null, ["63001"],
            AlcanceProyecto.MUNICIPAL, "Reconstrucción de Escuela La Esperanza",
            Resumen with { Estado = EstadoProyecto.FORMULADO, Presupuesto = null, CostoEjecutado = 0, Avance = 0, Version = 1 }),
        ["EV-19"] = new PresupuestoCambiadoV1(Proyecto, TipoCambioProyecto.PRESUPUESTO, null, 850_000_000m, null, null, 2,
            "alcaldia.armenia", "Asignación inicial", Resumen),
        ["EV-20"] = new EstadoOAvanceCambiadoV1(Proyecto, TipoCambioProyecto.COSTO, EstadoProyecto.EN_EJECUCION,
            EstadoProyecto.EN_EJECUCION, 35m, 120_000_000m, 20_000_000m, "100000000", "120000000", 7, "ing.perez",
            "Compra de materiales", Resumen),
        ["EV-21"] = new CapaGeograficaActualizadaV1(CapaGeografica.MUNICIPIOS, 1,
            [new Division("63001", "Armenia", "63", Poligono())], "DANE MGN", 1, 1),
        ["EV-22"] = new ConflictoResueltoV1(Guid.NewGuid(), DecisionConflicto.PREVALECE_ENTRANTE, Evaluacion, 3,
            "operador.1", "La evaluación entrante tiene evidencia más reciente", Instantanea()),
        ["EV-23"] = new EstadoIntegracionCambiadoV1("SE-7", EstadoIntegracion.CERRADO, EstadoIntegracion.ABIERTO, Instante, 12),
        ["EV-24"] = new ReporteExternoProyectoV1(Proyecto, TipoReporteExterno.AVANCE, 40m, null, Instante, "se7:avance:9921", "9921"),
        ["EV-25"] = new TipoActivoActualizadoV1("ESCUELA", "Escuela",
        [
            new CriterioNivel(NivelDano.SIN_DANO, "Sin daños visibles"),
            new CriterioNivel(NivelDano.LEVE, "Fisuras en muros no estructurales"),
            new CriterioNivel(NivelDano.MODERADO, "Daño en elementos no estructurales que impide el uso parcial"),
            new CriterioNivel(NivelDano.GRAVE, "Daño estructural; no se puede usar"),
            new CriterioNivel(NivelDano.DESTRUCCION_TOTAL, "Colapso total o parcial"),
        ], 2),
        ["EV-26"] = new EvaluacionActualizadaV1(Evaluacion, MotivoActualizacion.ASOCIACION, "operador.1", Instantanea()),
        ["EV-27"] = new OrganismosDesastreCambiadosV1(Desastre, "ungrd",
            [new Participante("defensa-civil", "Evaluación de daños"), new Participante("alcaldia.armenia", "Autoridad local")],
            2, "coordinador.quindio"),
    };

    public static Sobre Sobre(string tipo, string? clave = null)
    {
        DefinicionEvento definicion = CatalogoEventos.PorTipo(tipo);
        return Eventos.Sobre.Crear(definicion, definicion.Productor == "adaptador-*" ? "adaptador-se7" : definicion.Productor,
            clave ?? $"ejemplo:{tipo}", Datos[tipo], Instante, idCorrelacion: "prueba");
    }
}
