using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;

namespace AdaptadorSe7.Pruebas;

/// <summary>Eventos de ejemplo, válidos según el contrato (como los de EjemplosContrato de comun).</summary>
public static class Ejemplos
{
    public static readonly Guid Proyecto = Guid.Parse("5f0c1c52-6a43-4a43-9a7f-0f0a3d1b2c01");
    public static readonly Guid Desastre = Guid.Parse("1b7d2f7e-3c1a-4c55-8e6f-2a9e1d0c4b11");
    public static readonly Guid Evaluacion = Guid.Parse("9d3e4f5a-6b7c-4d8e-9f0a-1b2c3d4e5f60");
    public static readonly DateTimeOffset Instante = new(2026, 11, 16, 15, 30, 0, TimeSpan.Zero);

    public static ResumenProyecto Resumen(EstadoProyecto estado = EstadoProyecto.FORMULADO, decimal avance = 0, int version = 1) =>
        new(Desastre, "Reconstrucción de Escuela La Esperanza", estado, 850_000_000m, 0m, avance, version);

    public static Sobre ProyectoCreado(Guid? proyecto = null)
    {
        Guid id = proyecto ?? Proyecto;
        ProyectoCreadoV1 datos = new(id, [Evaluacion], Desastre, 850_000_000m, "SGR", "ing.perez",
            new Cronograma(new DateOnly(2027, 1, 15), new DateOnly(2027, 12, 15)), ["63001"], AlcanceProyecto.MUNICIPAL,
            "Reconstrucción de Escuela La Esperanza", Resumen(), "ungrd.operador");
        return Sobre.Crear(CatalogoEventos.ProyectoCreado, CatalogoEventos.Reconstruccion, $"proyecto:{id}:v1", datos, Instante);
    }

    public static Sobre Cambio(TipoCambioProyecto tipo, int version, string? valorNuevo, Procedencia? procedencia = null,
        EstadoProyecto estado = EstadoProyecto.EN_EJECUCION, decimal avance = 35m, decimal? costo = null)
    {
        EstadoOAvanceCambiadoV1 datos = new(Proyecto, tipo, estado, estado, avance, 120_000_000m, costo, null, valorNuevo, version,
            "ing.perez", "Registro de obra", Resumen(estado, avance, version));
        return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{Proyecto}:v{version}",
            datos, Instante, procedencia);
    }
}

public class PruebasTraduccion
{
    private readonly ValidadorContrato _contrato = ValidadorContrato.Instancia;

    [Fact]
    public void EV18_es_el_alta_de_la_obra_con_la_version_del_proyecto()
    {
        Sobre sobre = Ejemplos.ProyectoCreado();
        _contrato.Validar(sobre);

        PeticionSe7 peticion = TraduccionSe7.ASe7(sobre)!;

        Assert.Equal(Ejemplos.Proyecto.ToString(), peticion.CodigoObra);
        Assert.Equal(1, peticion.Version);
        Assert.Equal($"v1/obras/{Ejemplos.Proyecto}/versiones/1", peticion.Ruta);
        Assert.Equal("ALTA", peticion.Cuerpo.Operacion);
        Assert.Equal(SituacionSe7.Formulada, peticion.Cuerpo.Situacion);
        Assert.Equal("Reconstrucción de Escuela La Esperanza", peticion.Cuerpo.Titulo);
        Assert.Equal(Ejemplos.Desastre.ToString(), peticion.Cuerpo.CodigoDesastre);
        Assert.Equal(["63001"], peticion.Cuerpo.Municipios!);
        Assert.Equal(850_000_000m, peticion.Cuerpo.Presupuesto);
        Assert.Equal("SGR", peticion.Cuerpo.FuenteRecursos);
        Assert.Equal(new DateOnly(2027, 12, 15), peticion.Cuerpo.FechaFin);
    }

    [Fact]
    public void EV20_es_una_modificacion_con_la_version_del_historial()
    {
        Sobre sobre = Ejemplos.Cambio(TipoCambioProyecto.CONTRATISTA, 4, "Constructora Andina");
        _contrato.Validar(sobre);

        PeticionSe7 peticion = TraduccionSe7.ASe7(sobre)!;

        Assert.Equal(4, peticion.Version);
        Assert.Equal("MODIFICACION", peticion.Cuerpo.Operacion);
        Assert.Equal("CONTRATISTA", peticion.Cuerpo.Concepto);
        Assert.Equal("Constructora Andina", peticion.Cuerpo.Contratista);
        Assert.Equal(SituacionSe7.EnObra, peticion.Cuerpo.Situacion);
        Assert.Equal(35m, peticion.Cuerpo.PorcentajeAvance);
        Assert.Equal("Registro de obra", peticion.Cuerpo.Nota);

        Sobre costo = Ejemplos.Cambio(TipoCambioProyecto.COSTO, 7, "120000000", costo: 20_000_000m);
        _contrato.Validar(costo);
        Assert.Equal(7, TraduccionSe7.ASe7(costo)!.Version);
        Assert.Null(TraduccionSe7.ASe7(costo)!.Cuerpo.Contratista);
    }

    [Fact]
    public void La_misma_version_da_siempre_la_misma_clave_hacia_SE7()
    {
        PeticionSe7 una = TraduccionSe7.ASe7(Ejemplos.Cambio(TipoCambioProyecto.AVANCE, 5, "40"))!;
        PeticionSe7 otra = TraduccionSe7.ASe7(Ejemplos.Cambio(TipoCambioProyecto.AVANCE, 5, "40"))!;
        Assert.Equal(una.Ruta, otra.Ruta);
    }

    [Fact]
    public void Lo_que_SE7_reporto_no_se_le_reenvia_y_otros_tipos_se_omiten()
    {
        Sobre eco = Ejemplos.Cambio(TipoCambioProyecto.AVANCE, 6, "45", Procedencia.DeSistemaExterno("SE-7"));
        _contrato.Validar(eco);
        Assert.Null(TraduccionSe7.ASe7(eco));
        // Un cambio con procedencia de otro sistema sí se envía.
        Assert.NotNull(TraduccionSe7.ASe7(Ejemplos.Cambio(TipoCambioProyecto.COSTO, 6, "1", Procedencia.DeSistemaExterno("SE-6"), costo: 1m)));

        Sobre otro = Sobre.Crear(CatalogoEventos.EstadoIntegracionCambiado, "adaptador-se7", "x",
            new EstadoIntegracionCambiadoV1("SE-7", EstadoIntegracion.CERRADO, EstadoIntegracion.ABIERTO, Ejemplos.Instante, 0), Ejemplos.Instante);
        Assert.Null(TraduccionSe7.ASe7(otro));
    }

    [Theory]
    [InlineData(EstadoProyecto.FORMULADO, "FORMULADA")]
    [InlineData(EstadoProyecto.APROBADO, "APROBADA")]
    [InlineData(EstadoProyecto.EN_EJECUCION, "EN_OBRA")]
    [InlineData(EstadoProyecto.FINALIZADO, "TERMINADA")]
    [InlineData(EstadoProyecto.CANCELADO, "ANULADA")]
    public void Cada_estado_tiene_su_situacion_en_SE7(EstadoProyecto estado, string situacion)
    {
        Assert.Equal(situacion, TraduccionSe7.Situacion(estado));
        Assert.Contains(situacion, SimSe7.Situaciones.Todas);   // el simulador la acepta
    }

    [Fact]
    public void Un_avance_de_SE7_es_un_EV24_valido_con_su_marca_y_su_clave_de_origen()
    {
        CambioObraSe7 cambio = new(9921, Ejemplos.Proyecto.ToString(), "AVANCE", 40m, null, Ejemplos.Instante);

        Sobre sobre = TraduccionSe7.AEvento(cambio, Ejemplos.Instante.AddSeconds(5))!;

        _contrato.Validar(sobre);
        Assert.Equal("EV-24", sobre.Tipo);
        Assert.Equal("adaptador-se7", sobre.Productor);
        Assert.Equal("sistema-externo", sobre.Procedencia.Fuente);
        Assert.Equal("SE-7", sobre.Procedencia.SistemaOrigen);
        Assert.Equal("se7:avance:9921", sobre.ClaveIdempotencia);
        ReporteExternoProyectoV1 datos = sobre.LeerDatos<ReporteExternoProyectoV1>();
        Assert.Equal(Ejemplos.Proyecto, datos.IdProyecto);
        Assert.Equal(TipoReporteExterno.AVANCE, datos.Tipo);
        Assert.Equal(40m, datos.Avance);
        Assert.Null(datos.Contratista);
        Assert.Equal("9921", datos.MarcaAvance);
        Assert.Equal("se7:avance:9921", datos.ClaveOrigen);
        Assert.Equal(Ejemplos.Instante, datos.InstanteExterno);

        // El mismo cambio, traducido otra vez (otra ronda, otro instante), tiene la misma clave.
        Assert.Equal(sobre.ClaveIdempotencia, TraduccionSe7.AEvento(cambio, Ejemplos.Instante.AddHours(1))!.ClaveIdempotencia);
    }

    [Fact]
    public void Un_contratista_de_SE7_es_un_EV24_valido()
    {
        Sobre sobre = TraduccionSe7.AEvento(new CambioObraSe7(12, Ejemplos.Proyecto.ToString(), "CONTRATISTA", null,
            "Constructora 001 S.A.S.", Ejemplos.Instante), Ejemplos.Instante)!;

        _contrato.Validar(sobre);
        ReporteExternoProyectoV1 datos = sobre.LeerDatos<ReporteExternoProyectoV1>();
        Assert.Equal(TipoReporteExterno.CONTRATISTA, datos.Tipo);
        Assert.Equal("Constructora 001 S.A.S.", datos.Contratista);
        Assert.Null(datos.Avance);
        Assert.Equal("se7:contratista:12", sobre.ClaveIdempotencia);
    }

    [Theory]
    [InlineData("OBRA-PROPIA-77", "AVANCE", 10.0, null)]       // obra que no es de la plataforma
    [InlineData(null, "AVANCE", null, null)]                   // avance sin porcentaje
    [InlineData(null, "AVANCE", 120.0, null)]                  // fuera de rango
    [InlineData(null, "CONTRATISTA", null, " ")]               // contratista vacío
    [InlineData(null, "CIERRE", null, null)]                   // clase que la plataforma no conoce
    public void Un_cambio_sin_equivalente_en_el_contrato_no_produce_evento(string? obra, string clase, double? avance, string? contratista)
    {
        CambioObraSe7 cambio = new(1, obra ?? Ejemplos.Proyecto.ToString(), clase, (decimal?)avance, contratista, Ejemplos.Instante);
        Assert.Null(TraduccionSe7.AEvento(cambio, Ejemplos.Instante));
    }

    [Fact]
    public void EV23_de_una_transicion_cumple_el_contrato()
    {
        TransicionCircuito abre = new(Guid.NewGuid(), "SE-7", EstadoIntegracion.CERRADO, EstadoIntegracion.ABIERTO, Ejemplos.Instante);

        Sobre sobre = TraduccionEstadoIntegracion.ASobre(abre, "adaptador-se7", 42);

        _contrato.Validar(sobre);
        EstadoIntegracionCambiadoV1 datos = sobre.LeerDatos<EstadoIntegracionCambiadoV1>();
        Assert.Equal("SE-7", datos.SistemaExterno);
        Assert.Equal(EstadoIntegracion.ABIERTO, datos.EstadoNuevo);
        Assert.Equal(42, datos.MensajesEnCola);
        Assert.Equal($"integracion:SE-7:{abre.Id}", sobre.ClaveIdempotencia);
        Assert.Equal(abre.Id, sobre.IdEvento);
    }
}
