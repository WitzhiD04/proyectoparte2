using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;
using Reconstruccion.Infraestructura.Persistencia;
using NivelDano = Reconstruccion.Dominio.NivelDano;

namespace Reconstruccion.Integracion;

/// <summary>
/// La jurisdicción de las lecturas (Cuadro 3 del SRS, DA-14) con los adaptadores reales: el repositorio devuelve los
/// municipios de las evaluaciones de origen, y con ellos la lista, el detalle y el historial filtran por territorio.
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
}
