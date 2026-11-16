using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Dominio;
using Reconstruccion.Infraestructura.Mensajeria;
using Reconstruccion.Infraestructura.Persistencia;
using Cronograma = Reconstruccion.Dominio.Cronograma;
using EstadoCanonico = Plataforma.Comun.Eventos.EstadoProyecto;
using EstadoDominio = Reconstruccion.Dominio.EstadoProyecto;
using NivelDano = Reconstruccion.Dominio.NivelDano;

namespace Reconstruccion.Integracion;

/// <summary>Los hechos de un proyecto se traducen a EV-18, EV-19 y EV-20 que cumplen el contrato (E-10).</summary>
public class PruebasEventos : IClassFixture<BaseMigrada>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor Autoridad = new("alcaldia.armenia", Rol.Autoridad, Ambito.Local);
    private static readonly Actor Responsable = new("ing.perez", Rol.Responsable);

    private readonly BaseMigrada _base;
    public PruebasEventos(BaseMigrada baseMigrada) { _base = baseMigrada; }

    /// <summary>Un proyecto recorre su ciclo y se guardan los sobres de cada paso.</summary>
    private static List<Sobre> CicloCompleto()
    {
        List<Sobre> sobres = new();
        void Tomar(ProyectoReconstruccion p)
        {
            sobres.AddRange(HechosDeProyecto.De(p).Select(h => PublicadorHechosBandeja.Traducir(h, "prueba")));
            p.LimpiarPendientes();
        }

        ProyectoReconstruccion p = ProyectoReconstruccion.Crear(Guid.NewGuid(), Guid.NewGuid(), "Reconstrucción de Puente El Alambrado",
            AlcanceTerritorial.Municipal, [new EvaluacionOrigen(Guid.NewGuid(), Guid.NewGuid(), "Puente", NivelDano.DestruccionTotal, "63001")],
            Autoridad, Ahora);
        Tomar(p);
        p.AsignarPresupuesto(Autoridad, new Dinero(900_000_000), "Fondo de reconstrucción",
            new Cronograma(new DateOnly(2027, 1, 15), new DateOnly(2027, 12, 15)), "Asignación inicial", Ahora);
        p.AsignarResponsable(Autoridad, Responsable.Usuario, "Asignación inicial", Ahora);
        Tomar(p);
        p.Aprobar(Autoridad, Ahora);
        p.IniciarEjecucion(Responsable, "Constructora Andina", Ahora);
        Tomar(p);
        p.RegistrarAvance(Responsable, 40, null, Ahora);
        p.RegistrarCosto(Responsable, new Dinero(150_000_000), "Cimentación", Ahora);
        Tomar(p);
        return sobres;
    }

    [Fact]
    public void Cada_hecho_produce_un_evento_que_cumple_el_contrato()
    {
        List<Sobre> sobres = CicloCompleto();
        foreach (Sobre sobre in sobres)
        {
            ValidadorContrato.Instancia.Validar(sobre);
            Assert.Equal("reconstruccion", sobre.Productor);
            Assert.Equal("prueba", sobre.IdCorrelacion);
        }
        Assert.Equal(sobres.Count, sobres.Select(s => s.ClaveIdempotencia).Distinct().Count());
    }

    [Fact]
    public void El_reparto_de_tipos_sigue_la_enmienda_E10()
    {
        List<Sobre> sobres = CicloCompleto();
        Sobre creado = Assert.Single(sobres, s => s.Tipo == "EV-18");
        ProyectoCreadoV1 datos = creado.LeerDatos<ProyectoCreadoV1>();
        Assert.Single(datos.IdsEvaluacionesOrigen);
        Assert.Equal(["63001"], datos.CodigosMunicipio);
        Assert.Equal(AlcanceProyecto.MUNICIPAL, datos.Alcance);

        // Presupuesto y fuente van en EV-19; responsable, cronograma, contratista, estado, avance y costo en EV-20.
        List<TipoCambioProyecto> en19 = sobres.Where(s => s.Tipo == "EV-19").Select(s => s.LeerDatos<PresupuestoCambiadoV1>().TipoCambio).ToList();
        Assert.Equal([TipoCambioProyecto.PRESUPUESTO, TipoCambioProyecto.FUENTE], en19);
        List<TipoCambioProyecto> en20 = sobres.Where(s => s.Tipo == "EV-20").Select(s => s.LeerDatos<EstadoOAvanceCambiadoV1>().TipoCambio).ToList();
        Assert.Contains(TipoCambioProyecto.CRONOGRAMA, en20);
        Assert.Contains(TipoCambioProyecto.RESPONSABLE, en20);
        Assert.Contains(TipoCambioProyecto.ESTADO, en20);
        Assert.Contains(TipoCambioProyecto.CONTRATISTA, en20);
        Assert.Contains(TipoCambioProyecto.AVANCE, en20);
        Assert.Contains(TipoCambioProyecto.COSTO, en20);
    }

    [Fact]
    public void El_costo_trae_el_monto_registrado_y_la_aprobacion_el_estado_anterior()
    {
        List<Sobre> sobres = CicloCompleto();
        List<EstadoOAvanceCambiadoV1> cambios = sobres.Where(s => s.Tipo == "EV-20").Select(s => s.LeerDatos<EstadoOAvanceCambiadoV1>()).ToList();

        EstadoOAvanceCambiadoV1 costo = Assert.Single(cambios, c => c.TipoCambio == TipoCambioProyecto.COSTO);
        Assert.Equal(150_000_000m, costo.CostoRegistrado);
        Assert.Equal(150_000_000m, costo.CostoEjecutado);
        Assert.All(cambios.Where(c => c.TipoCambio != TipoCambioProyecto.COSTO), c => Assert.Null(c.CostoRegistrado));

        EstadoOAvanceCambiadoV1 aprobacion = cambios.First(c => c.TipoCambio == TipoCambioProyecto.ESTADO);
        Assert.Equal(EstadoCanonico.FORMULADO, aprobacion.EstadoAnterior);

        PresupuestoCambiadoV1 presupuesto = sobres.First(s => s.Tipo == "EV-19").LeerDatos<PresupuestoCambiadoV1>();
        Assert.Null(presupuesto.PresupuestoAnterior);
        Assert.Equal(900_000_000m, presupuesto.PresupuestoNuevo);
    }

    [Fact]
    public async Task La_proyeccion_aplica_los_eventos_sin_importar_el_orden_ni_las_repeticiones()
    {
        List<Sobre> sobres = CicloCompleto();
        // Repetidos y al revés: la protección por versión conserva el estado más nuevo.
        IEnumerable<Sobre> desordenados = sobres.AsEnumerable().Reverse().Concat(sobres);

        await using ProgramaDbContext db = new(new DbContextOptionsBuilder<ProgramaDbContext>().UseNpgsql(_base.AppPrograma).Options);
        ManejadorEventosPrograma manejador = new(new ActualizarProyeccionPrograma(new ProyeccionProgramaEf(db)),
            NullLogger<ManejadorEventosPrograma>.Instance);
        foreach (Sobre sobre in desordenados)
        {
            await manejador.ManejarAsync(sobre, default);
        }

        Guid idProyecto = sobres[0].LeerDatos<ProyectoCreadoV1>().IdProyecto;
        FilaProgramaFila fila = await db.Proyectos.AsNoTracking().SingleAsync(f => f.IdProyecto == idProyecto);
        Assert.Equal(nameof(EstadoDominio.EnEjecucion), fila.Estado);
        Assert.Equal(40m, fila.Avance);
        Assert.Equal(150_000_000m, fila.CostoEjecutado);
        Assert.Equal(900_000_000m, fila.Presupuesto);
        Assert.Equal(Ahora, fila.ActualizadoEn);
    }
}
