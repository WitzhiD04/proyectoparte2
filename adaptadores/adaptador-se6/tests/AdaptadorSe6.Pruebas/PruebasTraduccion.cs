using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;

namespace AdaptadorSe6.Pruebas;

/// <summary>Eventos de ejemplo, válidos según el contrato (como los de EjemplosContrato de comun).</summary>
public static class Ejemplos
{
    public static readonly Guid Proyecto = Guid.Parse("5f0c1c52-6a43-4a43-9a7f-0f0a3d1b2c01");
    public static readonly Guid Desastre = Guid.Parse("1b7d2f7e-3c1a-4c55-8e6f-2a9e1d0c4b11");
    public static readonly Guid Evaluacion = Guid.Parse("9d3e4f5a-6b7c-4d8e-9f0a-1b2c3d4e5f60");
    public static readonly DateTimeOffset Instante = new(2026, 11, 16, 15, 30, 0, TimeSpan.Zero);

    public static ResumenProyecto Resumen(
        EstadoProyecto estado = EstadoProyecto.FORMULADO,
        decimal? presupuesto = 850_000_000m,
        decimal costoEjecutado = 0m,
        decimal avance = 0m,
        int version = 1) =>
        new(Desastre, "Reconstrucción de Escuela La Esperanza", estado, presupuesto, costoEjecutado, avance, version);

    public static Sobre ProyectoCreado(Guid? proyecto = null, decimal? presupuesto = 850_000_000m, string? fuente = "SGR")
    {
        Guid id = proyecto ?? Proyecto;
        ProyectoCreadoV1 datos = new(id, [Evaluacion], Desastre, presupuesto, fuente, "ing.perez",
            new Cronograma(new DateOnly(2027, 1, 15), new DateOnly(2027, 12, 15)), ["63001"], AlcanceProyecto.MUNICIPAL,
            "Reconstrucción de Escuela La Esperanza", Resumen(presupuesto: presupuesto), "ungrd.operador");
        return Sobre.Crear(CatalogoEventos.ProyectoCreado, CatalogoEventos.Reconstruccion, $"proyecto:{id}:v1", datos, Instante);
    }

    public static Sobre PresupuestoCambiado(
        int version = 2,
        TipoCambioProyecto tipo = TipoCambioProyecto.PRESUPUESTO,
        decimal? anterior = 850_000_000m,
        decimal? nuevo = 950_000_000m,
        string? fuenteAnterior = "SGR",
        string? fuenteNueva = "SGR",
        Guid? proyecto = null)
    {
        Guid id = proyecto ?? Proyecto;
        PresupuestoCambiadoV1 datos = new(id, tipo, anterior, nuevo, fuenteAnterior, fuenteNueva, version,
            "alcaldia.armenia", "Ajuste presupuestal", Resumen(EstadoProyecto.APROBADO, nuevo, 0m, 0m, version));
        return Sobre.Crear(CatalogoEventos.PresupuestoCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{id}:v{version}", datos, Instante);
    }

    public static Sobre Cambio(
        TipoCambioProyecto tipo,
        int version,
        string? valorNuevo,
        EstadoProyecto estadoAnterior = EstadoProyecto.EN_EJECUCION,
        EstadoProyecto estadoNuevo = EstadoProyecto.EN_EJECUCION,
        decimal avance = 35m,
        decimal costoEjecutado = 120_000_000m,
        decimal? costoRegistrado = null,
        Procedencia? procedencia = null,
        Guid? proyecto = null)
    {
        Guid id = proyecto ?? Proyecto;
        EstadoOAvanceCambiadoV1 datos = new(id, tipo, estadoAnterior, estadoNuevo, avance, costoEjecutado, costoRegistrado,
            null, valorNuevo, version, "ing.perez", "Registro de obra",
            Resumen(estadoNuevo, 950_000_000m, costoEjecutado, avance, version));
        return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{id}:v{version}",
            datos, Instante, procedencia);
    }
}

public class PruebasTraduccion
{
    private readonly ValidadorContrato _contrato = ValidadorContrato.Instancia;

    [Fact]
    public void EV18_es_la_apertura_financiera_con_la_version_del_proyecto()
    {
        Sobre sobre = Ejemplos.ProyectoCreado();
        _contrato.Validar(sobre);

        MovimientoSe6 mov = TraduccionSe6.AMovimiento(sobre)!;

        Assert.Equal(Ejemplos.Proyecto.ToString(), mov.CodigoProyecto);
        Assert.Equal(1, mov.VersionHistorial);
        Assert.Equal(ClaseMovimientoSe6.Apertura, mov.Clase);
        Assert.Equal(SituacionSe6.Abierta, mov.Situacion);
        Assert.Equal("Reconstrucción de Escuela La Esperanza", mov.TituloProyecto);
        Assert.Equal(Ejemplos.Desastre.ToString(), mov.CodigoDesastre);
        Assert.Equal(850_000_000m, mov.Presupuesto);
        Assert.Equal("SGR", mov.FuenteFinanciacion);
        Assert.Equal(850_000_000m, mov.MontoMovimiento);
        Assert.Equal(0m, mov.CostoAcumulado);
        Assert.Equal("ungrd.operador", mov.Autor);
    }

    [Fact]
    public void EV19_envia_presupuesto_y_fuente_con_versionHistorial_E10()
    {
        Sobre sobrePresupuesto = Ejemplos.PresupuestoCambiado(2, TipoCambioProyecto.PRESUPUESTO, 850_000_000m, 950_000_000m);
        _contrato.Validar(sobrePresupuesto);

        MovimientoSe6 movPres = TraduccionSe6.AMovimiento(sobrePresupuesto)!;
        Assert.Equal(2, movPres.VersionHistorial);
        Assert.Equal(ClaseMovimientoSe6.Presupuesto, movPres.Clase);
        Assert.Equal(950_000_000m, movPres.Presupuesto);
        Assert.Equal(950_000_000m, movPres.MontoMovimiento);
        Assert.Equal("SGR", movPres.FuenteFinanciacion);
        Assert.Equal("Ajuste presupuestal", movPres.Nota);

        Sobre sobreFuente = Ejemplos.PresupuestoCambiado(3, TipoCambioProyecto.FUENTE, 950_000_000m, 950_000_000m, "SGR", "UNGRD");
        _contrato.Validar(sobreFuente);

        MovimientoSe6 movFuente = TraduccionSe6.AMovimiento(sobreFuente)!;
        Assert.Equal(3, movFuente.VersionHistorial);
        Assert.Equal(ClaseMovimientoSe6.Fuente, movFuente.Clase);
        Assert.Equal("UNGRD", movFuente.FuenteFinanciacion);
        Assert.Null(movFuente.MontoMovimiento);
    }

    [Fact]
    public void Del_EV20_envia_el_costo_con_costoRegistrado_y_el_cierre_cuando_se_finaliza_o_cancela()
    {
        Sobre costo = Ejemplos.Cambio(TipoCambioProyecto.COSTO, 5, "120000000", costoEjecutado: 120_000_000m, costoRegistrado: 45_000_000m);
        _contrato.Validar(costo);

        MovimientoSe6 movCosto = TraduccionSe6.AMovimiento(costo)!;
        Assert.Equal(5, movCosto.VersionHistorial);
        Assert.Equal(ClaseMovimientoSe6.Costo, movCosto.Clase);
        Assert.Equal(45_000_000m, movCosto.MontoMovimiento);
        Assert.Equal(120_000_000m, movCosto.CostoAcumulado);
        Assert.Equal(SituacionSe6.EnEjecucion, movCosto.Situacion);

        Sobre finalizado = Ejemplos.Cambio(TipoCambioProyecto.ESTADO, 8, "Finalizado",
            EstadoProyecto.EN_EJECUCION, EstadoProyecto.FINALIZADO, avance: 100m, costoEjecutado: 900_000_000m);
        _contrato.Validar(finalizado);

        MovimientoSe6 movFin = TraduccionSe6.AMovimiento(finalizado)!;
        Assert.Equal(8, movFin.VersionHistorial);
        Assert.Equal(ClaseMovimientoSe6.Finalizacion, movFin.Clase);
        Assert.Equal(SituacionSe6.Liquidada, movFin.Situacion);
        Assert.Equal(900_000_000m, movFin.CostoAcumulado);

        Sobre cancelado = Ejemplos.Cambio(TipoCambioProyecto.ESTADO, 9, "Cancelado",
            EstadoProyecto.EN_EJECUCION, EstadoProyecto.CANCELADO, avance: 35m, costoEjecutado: 120_000_000m);
        _contrato.Validar(cancelado);

        MovimientoSe6 movCanc = TraduccionSe6.AMovimiento(cancelado)!;
        Assert.Equal(9, movCanc.VersionHistorial);
        Assert.Equal(ClaseMovimientoSe6.Cancelacion, movCanc.Clase);
        Assert.Equal(SituacionSe6.Anulada, movCanc.Situacion);
    }

    [Fact]
    public void Del_EV20_ignora_avance_contratista_responsable_cronograma_documento_y_estados_no_finales()
    {
        Sobre avance = Ejemplos.Cambio(TipoCambioProyecto.AVANCE, 4, "50", avance: 50m);
        Sobre contratista = Ejemplos.Cambio(TipoCambioProyecto.CONTRATISTA, 5, "Constructora Andina");
        Sobre responsable = Ejemplos.Cambio(TipoCambioProyecto.RESPONSABLE, 6, "ing.gomez");
        Sobre cronograma = Ejemplos.Cambio(TipoCambioProyecto.CRONOGRAMA, 7, "2027-01-15..2027-12-31");
        Sobre documento = Ejemplos.Cambio(TipoCambioProyecto.DOCUMENTO, 8, "acta.pdf");
        Sobre aprobado = Ejemplos.Cambio(TipoCambioProyecto.ESTADO, 9, "Aprobado", EstadoProyecto.FORMULADO, EstadoProyecto.APROBADO);
        Sobre enEjecucion = Ejemplos.Cambio(TipoCambioProyecto.ESTADO, 10, "EnEjecucion", EstadoProyecto.APROBADO, EstadoProyecto.EN_EJECUCION);

        foreach (Sobre ignorado in new[] { avance, contratista, responsable, cronograma, documento, aprobado, enEjecucion })
        {
            _contrato.Validar(ignorado);
            Assert.Null(TraduccionSe6.AMovimiento(ignorado));
        }

        Assert.Null(TraduccionSe6.ALote([avance, contratista, aprobado]));
    }

    [Fact]
    public void ALote_filtra_ignorados_descarta_duplicados_y_calcula_id_determinista()
    {
        Sobre creado = Ejemplos.ProyectoCreado();
        Sobre avance = Ejemplos.Cambio(TipoCambioProyecto.AVANCE, 2, "20");
        Sobre costo = Ejemplos.Cambio(TipoCambioProyecto.COSTO, 3, "50000000", costoRegistrado: 50_000_000m);
        Sobre costoDuplicado = Ejemplos.Cambio(TipoCambioProyecto.COSTO, 3, "50000000", costoRegistrado: 50_000_000m);

        PeticionLoteSe6 lote1 = TraduccionSe6.ALote([creado, avance, costo, costoDuplicado])!;
        PeticionLoteSe6 lote2 = TraduccionSe6.ALote([creado, costo])!;

        Assert.Equal(2, lote1.Cuerpo.Movimientos.Count);
        Assert.Equal([1, 3], lote1.Cuerpo.Movimientos.Select(m => m.VersionHistorial));
        Assert.Equal(lote1.IdLote, lote2.IdLote);
        Assert.Equal(lote1.Ruta, lote2.Ruta);
    }

    [Fact]
    public void El_eco_de_SE6_y_otros_eventos_se_omiten()
    {
        Sobre eco = Ejemplos.Cambio(TipoCambioProyecto.COSTO, 6, "100", costoRegistrado: 100m,
            procedencia: Procedencia.DeSistemaExterno("SE-6"));
        _contrato.Validar(eco);
        Assert.Null(TraduccionSe6.AMovimiento(eco));

        Sobre ev23 = Sobre.Crear(CatalogoEventos.EstadoIntegracionCambiado, "adaptador-se6", "x",
            new EstadoIntegracionCambiadoV1("SE-6", EstadoIntegracion.CERRADO, EstadoIntegracion.ABIERTO, Ejemplos.Instante, 0),
            Ejemplos.Instante);
        Assert.Null(TraduccionSe6.AMovimiento(ev23));
    }

    [Theory]
    [InlineData(EstadoProyecto.FORMULADO, "ABIERTA")]
    [InlineData(EstadoProyecto.APROBADO, "COMPROMETIDA")]
    [InlineData(EstadoProyecto.EN_EJECUCION, "EN_EJECUCION")]
    [InlineData(EstadoProyecto.FINALIZADO, "LIQUIDADA")]
    [InlineData(EstadoProyecto.CANCELADO, "ANULADA")]
    public void Cada_estado_tiene_su_situacion_en_SE6(EstadoProyecto estado, string situacion)
    {
        Assert.Equal(situacion, TraduccionSe6.Situacion(estado));
        Assert.Contains(situacion, SimSe6.Situaciones.Todas);
    }

    [Fact]
    public void EV23_de_una_transicion_de_SE6_cumple_el_contrato()
    {
        TransicionCircuito abre = new(Guid.NewGuid(), "SE-6", EstadoIntegracion.CERRADO, EstadoIntegracion.ABIERTO, Ejemplos.Instante);

        Sobre sobre = TraduccionEstadoIntegracion.ASobre(abre, "adaptador-se6", 15);

        _contrato.Validar(sobre);
        EstadoIntegracionCambiadoV1 datos = sobre.LeerDatos<EstadoIntegracionCambiadoV1>();
        Assert.Equal("SE-6", datos.SistemaExterno);
        Assert.Equal(EstadoIntegracion.ABIERTO, datos.EstadoNuevo);
        Assert.Equal(15, datos.MensajesEnCola);
        Assert.Equal($"integracion:SE-6:{abre.Id}", sobre.ClaveIdempotencia);
    }
}
