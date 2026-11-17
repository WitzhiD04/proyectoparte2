using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;
using Reconstruccion.Infraestructura;
using Reconstruccion.Infraestructura.Persistencia;
using NivelDano = Reconstruccion.Dominio.NivelDano;

namespace Reconstruccion.Integracion;

/// <summary>
/// La jurisdicción de las lecturas y de las acciones de la autoridad (Cuadro 3 del SRS, DA-14, E-39) con los adaptadores
/// reales: el repositorio devuelve los municipios de las evaluaciones de origen, y con ellos la lista, el detalle y el
/// historial filtran por territorio, y asignar, aprobar y cancelar responden 404 a quien no ve el proyecto.
/// </summary>
public class PruebasJurisdiccion : IClassFixture<BaseMigrada>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor Armenia = new("alcaldia.armenia", Rol.Autoridad, Ambito.Local, "63001");
    private static readonly Actor Calarca = new("alcaldia.calarca", Rol.Autoridad, Ambito.Local, "63130");
    private static readonly Actor Quindio = new("gobernacion.quindio", Rol.Autoridad, Ambito.Regional, "63");
    private static readonly Actor Auditor = new("auditor.1", Rol.Auditor);

    private readonly BaseMigrada _base;
    public PruebasJurisdiccion(BaseMigrada baseMigrada) { _base = baseMigrada; }

    private ReconstruccionDbContext ComoApp() =>
        new(new DbContextOptionsBuilder<ReconstruccionDbContext>().UseNpgsql(_base.App).Options);

    /// <summary>Puerto de salida mínimo para leer lo que entrega el caso de uso.</summary>
    private sealed class Salida<T> : IPuertoSalida<T>
    {
        public string Resultado { get; private set; } = "";
        public T? Respuesta { get; private set; }
        public void Exito(T respuesta) { Resultado = "exito"; Respuesta = respuesta; }
        public void NoEncontrado(string mensaje) => Resultado = "no_encontrado";
        public void NoAutorizado(string mensaje) => Resultado = "no_autorizado";
        public void ReglaIncumplida(string codigo, string mensaje, IReadOnlyList<string>? detalles = null) => Resultado = "regla";
    }

    /// <summary>Guarda un proyecto de Armenia con su historial en un desastre propio de la prueba.</summary>
    private async Task<ProyectoReconstruccion> ProyectoDeArmeniaAsync()
    {
        ProyectoReconstruccion p = ProyectoReconstruccion.Crear(Guid.NewGuid(), Guid.NewGuid(), "Reconstrucción de la Escuela La Esperanza",
            AlcanceTerritorial.Municipal, [new EvaluacionOrigen(Guid.NewGuid(), Guid.NewGuid(), "Escuela", NivelDano.Grave, "63001")],
            Armenia, Ahora);
        await using ReconstruccionDbContext db = ComoApp();
        await new RepositorioProyectosEf(db).GuardarAsync(p, default);
        await new HistorialEf(db).AgregarAsync(p.EntradasNuevas, default);
        await db.SaveChangesAsync();
        return p;
    }

    [Theory]
    [InlineData("alcaldia.calarca", false)]
    [InlineData("alcaldia.armenia", true)]
    [InlineData("gobernacion.quindio", true)]
    [InlineData("auditor.1", true)]
    public async Task Lista_detalle_e_historial_filtran_por_territorio_con_la_base_real(string usuario, bool ve)
    {
        ProyectoReconstruccion p = await ProyectoDeArmeniaAsync();
        Actor actor = new[] { Armenia, Calarca, Quindio, Auditor }.Single(a => a.Usuario == usuario);
        await using ReconstruccionDbContext db = ComoApp();
        RepositorioProyectosEf repositorio = new(db);

        Salida<IReadOnlyList<ProyectoDto>> lista = new();
        await new ConsultarProyectos(repositorio).EjecutarAsync(new SolicitudProyectos(actor, p.IdDesastre), lista);
        Salida<ProyectoDto> detalle = new();
        await new ConsultarProyecto(repositorio).EjecutarAsync(new SolicitudProyecto(actor, p.Id), detalle);
        Salida<HistorialDto> historial = new();
        await new ConsultarHistorial(new HistorialEf(db), repositorio).EjecutarAsync(new SolicitudHistorial(actor, p.Id, null), historial);

        Assert.Equal(ve ? 1 : 0, lista.Respuesta!.Count);
        Assert.Equal(ve ? "exito" : "no_encontrado", detalle.Resultado);
        Assert.Equal(ve ? "exito" : "no_encontrado", historial.Resultado);
    }

    /// <summary>Casos de uso de escritura con los adaptadores reales (repositorio, historial, bandeja y unidad de trabajo).</summary>
    private ServiceProvider Servicios()
    {
        IConfiguration configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Reconstruccion"] = _base.App,
            ["Bus:Cola"] = "q.reconstruccion",
            ["Bus:Productor"] = "reconstruccion",
        }).Build();
        ServiceCollection s = new();
        s.AddLogging();
        s.AgregarInfraestructura(configuracion, Registro.RolEscritura);
        s.AddScoped<GuardadoDeProyectos>();
        s.AddScoped<AsignarPresupuesto>();
        s.AddScoped<AprobarProyecto>();
        s.AddScoped<CancelarProyecto>();
        return s.BuildServiceProvider();
    }

    /// <summary>Proyecto de Armenia listo para aprobar, guardado con su historial.</summary>
    private async Task<ProyectoReconstruccion> ProyectoDeArmeniaListoAsync()
    {
        ProyectoReconstruccion p = ProyectoReconstruccion.Crear(Guid.NewGuid(), Guid.NewGuid(), "Reconstrucción del Puente El Alambrado",
            AlcanceTerritorial.Municipal, [new EvaluacionOrigen(Guid.NewGuid(), Guid.NewGuid(), "Puente", NivelDano.DestruccionTotal, "63001")],
            Armenia, Ahora);
        p.AsignarPresupuesto(Armenia, new Dinero(900_000_000), "Fondo de reconstrucción",
            new Cronograma(new DateOnly(2027, 1, 15), new DateOnly(2027, 12, 15)), "Asignación inicial", Ahora);
        p.AsignarResponsable(Armenia, "ing.perez", "Asignación inicial", Ahora);
        await using ReconstruccionDbContext db = ComoApp();
        await new RepositorioProyectosEf(db).GuardarAsync(p, default);
        await new HistorialEf(db).AgregarAsync(p.EntradasNuevas, default);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Salida<ProyectoDto>> En<TCaso, TSolicitud>(ServiceProvider servicios, TSolicitud solicitud)
        where TCaso : IPuertoEntrada<TSolicitud, ProyectoDto>
    {
        await using AsyncServiceScope ambito = servicios.CreateAsyncScope();
        Salida<ProyectoDto> salida = new();
        await ambito.ServiceProvider.GetRequiredService<TCaso>().EjecutarAsync(solicitud, salida);
        return salida;
    }

    [Fact]
    public async Task Las_acciones_de_la_autoridad_respetan_la_jurisdiccion_con_la_base_real()
    {
        ProyectoReconstruccion p = await ProyectoDeArmeniaListoAsync();
        await using ServiceProvider servicios = Servicios();

        // Calarcá no ve el proyecto: asignar, aprobar y cancelar responden "no existe" (404).
        Assert.Equal("no_encontrado", (await En<AsignarPresupuesto, SolicitudAsignarPresupuesto>(servicios,
            new SolicitudAsignarPresupuesto(Calarca, p.Id, 1, null, null, null, "Cambio"))).Resultado);
        Assert.Equal("no_encontrado", (await En<AprobarProyecto, SolicitudAprobar>(servicios, new SolicitudAprobar(Calarca, p.Id))).Resultado);
        Assert.Equal("no_encontrado", (await En<CancelarProyecto, SolicitudCancelar>(servicios,
            new SolicitudCancelar(Calarca, p.Id, "No procede"))).Resultado);
        // La gobernación del Quindío lo ve, pero un municipal lo aprueba la alcaldía (403).
        Assert.Equal("no_autorizado", (await En<AprobarProyecto, SolicitudAprobar>(servicios, new SolicitudAprobar(Quindio, p.Id))).Resultado);
        // La alcaldía de Armenia sí lo aprueba, y queda guardado.
        Assert.Equal("exito", (await En<AprobarProyecto, SolicitudAprobar>(servicios, new SolicitudAprobar(Armenia, p.Id))).Resultado);

        await using ReconstruccionDbContext db = ComoApp();
        ProyectoReconstruccion? guardado = await new RepositorioProyectosEf(db).BuscarAsync(p.Id, default);
        Assert.Equal(EstadoProyecto.Aprobado, guardado!.Estado);
    }
}
