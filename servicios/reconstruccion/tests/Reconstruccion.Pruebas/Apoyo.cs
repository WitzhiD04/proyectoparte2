using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Pruebas;

/// <summary>Datos y actores que se repiten en las pruebas.</summary>
public static class Datos
{
    public static readonly Guid IdDesastre = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly DateTimeOffset Ahora = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    public static readonly Actor AutoridadLocal = new("alcaldia.armenia", Rol.Autoridad, Ambito.Local);
    public static readonly Actor AutoridadRegional = new("gobernacion.quindio", Rol.Autoridad, Ambito.Regional);
    public static readonly Actor Responsable = new("ing.perez", Rol.Responsable);
    public static readonly Actor OtroResponsable = new("ing.gomez", Rol.Responsable);
    public static readonly Actor Auditor = new("contraloria", Rol.Auditor);

    public static EvaluacionValidada Evaluacion(Guid? idActivo = null, NivelDano nivel = NivelDano.Grave,
        string municipio = "Armenia", string estado = EstadosEvaluacion.Validada, Guid? idDesastre = null)
    {
        return new EvaluacionValidada(Guid.NewGuid(), idDesastre ?? IdDesastre, idActivo ?? Guid.NewGuid(),
            "Escuela La Esperanza", "Escuela", nivel, municipio, CodigoDe(municipio), estado);
    }

    /// <summary>Códigos DIVIPOLA de los municipios que usan las pruebas.</summary>
    public static string CodigoDe(string municipio) => municipio switch
    {
        "Armenia" => "63001",
        "Calarcá" => "63130",
        "La Virginia" => "66400",
        _ => throw new ArgumentException($"Municipio sin código en las pruebas: {municipio}"),
    };

    public static ProyectoReconstruccion ProyectoFormulado(AlcanceTerritorial alcance = AlcanceTerritorial.Municipal)
    {
        EvaluacionOrigen origen = new(Guid.NewGuid(), Guid.NewGuid(), "Escuela", NivelDano.Grave, "63001");
        ProyectoReconstruccion p = ProyectoReconstruccion.Crear(Guid.NewGuid(), IdDesastre, "Reconstrucción de Escuela La Esperanza",
            alcance, new[] { origen }, AutoridadLocal, Ahora);
        return p;
    }

    public static ProyectoReconstruccion ProyectoListoParaAprobar(AlcanceTerritorial alcance = AlcanceTerritorial.Municipal)
    {
        ProyectoReconstruccion p = ProyectoFormulado(alcance);
        p.AsignarPresupuesto(AutoridadLocal, new Dinero(500_000_000), "Fondo de reconstrucción",
            new Cronograma(new DateOnly(2027, 1, 15), new DateOnly(2027, 9, 30)), "Asignación inicial", Ahora);
        p.AsignarResponsable(AutoridadLocal, Responsable.Usuario, "Asignación inicial", Ahora);
        return p;
    }

    public static ProyectoReconstruccion ProyectoAprobado()
    {
        ProyectoReconstruccion p = ProyectoListoParaAprobar();
        p.Aprobar(AutoridadLocal, Ahora);
        return p;
    }

    public static ProyectoReconstruccion ProyectoEnEjecucion()
    {
        ProyectoReconstruccion p = ProyectoAprobado();
        p.IniciarEjecucion(Responsable, "Constructora Andina", Ahora);
        return p;
    }
}

// ------------------------------------------------------------------ puertos simulados en memoria

public sealed class RelojFijo : IReloj
{
    public DateTimeOffset Ahora { get; set; } = Datos.Ahora;
}

public sealed class Memoria : IRepositorioProyectos, IHistorial, IPublicadorHechos, IUnidadDeTrabajo, ICopiasLocales, IAlmacenDocumentos, IProyeccionPrograma
{
    public Dictionary<Guid, InstantaneaProyecto> Proyectos { get; } = new();
    public List<EntradaHistorial> Historial { get; } = new();
    /// <summary>Hechos registrados por los casos de uso, después de confirmar la unidad de trabajo.</summary>
    public List<HechoProyecto> Bandeja { get; } = new();
    public Dictionary<string, LoteCreacion> Lotes { get; } = new();
    public List<DesastreLocal> Desastres { get; } = new();
    public List<EvaluacionValidada> Evaluaciones { get; } = new();
    public Dictionary<string, byte[]> Archivos { get; } = new();
    public Dictionary<Guid, FilaPrograma> Filas { get; } = new();
    public int Confirmaciones { get; private set; }

    // Lo pendiente solo pasa a los almacenes al confirmar: así se prueba que todo va en una transacción.
    private readonly List<Action> _pendiente = new();

    public Task<ProyectoReconstruccion?> BuscarAsync(Guid id, CancellationToken ct = default)
    {
        ProyectoReconstruccion? p = Proyectos.TryGetValue(id, out InstantaneaProyecto? datos) ? ProyectoReconstruccion.Rehidratar(Copiar(datos)) : null;
        return Task.FromResult(p);
    }

    public Task<IReadOnlyList<ProyectoReconstruccion>> DeDesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        IReadOnlyList<ProyectoReconstruccion> lista = Proyectos.Values.Where(d => d.IdDesastre == idDesastre)
            .Select(d => ProyectoReconstruccion.Rehidratar(Copiar(d))).ToList();
        return Task.FromResult(lista);
    }

    public Task<IReadOnlySet<Guid>> EvaluacionesVinculadasAsync(Guid idDesastre, CancellationToken ct = default)
    {
        IReadOnlySet<Guid> ids = Proyectos.Values.Where(d => d.IdDesastre == idDesastre)
            .SelectMany(d => d.Evaluaciones).Where(e => e.Vigente).Select(e => e.IdEvaluacion).ToHashSet();
        return Task.FromResult(ids);
    }

    public Task GuardarAsync(ProyectoReconstruccion proyecto, CancellationToken ct = default)
    {
        InstantaneaProyecto datos = Copiar(proyecto.AInstantanea());
        _pendiente.Add(() => Proyectos[datos.Id] = datos);
        return Task.CompletedTask;
    }

    public Task<LoteCreacion?> BuscarLoteAsync(string clave, CancellationToken ct = default)
    {
        return Task.FromResult(Lotes.TryGetValue(clave, out LoteCreacion? lote) ? lote : null);
    }

    public Task GuardarLoteAsync(LoteCreacion lote, CancellationToken ct = default)
    {
        _pendiente.Add(() => Lotes[lote.ClaveIdempotencia] = lote);
        return Task.CompletedTask;
    }

    public Task AgregarAsync(IEnumerable<EntradaHistorial> entradas, CancellationToken ct = default)
    {
        List<EntradaHistorial> copia = entradas.ToList();
        _pendiente.Add(() => Historial.AddRange(copia));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EntradaHistorial>> DeProyectoAsync(Guid idProyecto, DateTimeOffset? hasta = null, CancellationToken ct = default)
    {
        IReadOnlyList<EntradaHistorial> lista = Historial
            .Where(e => e.IdProyecto == idProyecto && (!hasta.HasValue || e.Instante <= hasta.Value)).ToList();
        return Task.FromResult(lista);
    }

    public Task RegistrarAsync(IEnumerable<HechoProyecto> hechos, CancellationToken ct = default)
    {
        List<HechoProyecto> copia = hechos.ToList();
        _pendiente.Add(() => Bandeja.AddRange(copia));
        return Task.CompletedTask;
    }

    public Task ConfirmarAsync(CancellationToken ct = default)
    {
        foreach (Action accion in _pendiente)
        {
            accion();
        }
        _pendiente.Clear();
        Confirmaciones++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DesastreLocal>> DesastresAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<DesastreLocal>>(Desastres);
    }

    public Task<DesastreLocal?> DesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        return Task.FromResult(Desastres.FirstOrDefault(d => d.Id == idDesastre));
    }

    public Task<IReadOnlyList<EvaluacionValidada>> EvaluacionesDeDesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        IReadOnlyList<EvaluacionValidada> lista = Evaluaciones.Where(e => e.IdDesastre == idDesastre).ToList();
        return Task.FromResult(lista);
    }

    public Task<string> GuardarAsync(string nombreObjeto, Stream contenido, long tamano, string tipoContenido, CancellationToken ct = default)
    {
        using MemoryStream copia = new();
        contenido.CopyTo(copia);
        Archivos[nombreObjeto] = copia.ToArray();
        return Task.FromResult(nombreObjeto);
    }

    public Task AplicarAsync(FilaPrograma fila, CancellationToken ct = default)
    {
        if (!Filas.TryGetValue(fila.IdProyecto, out FilaPrograma? actual) || fila.Version > actual.Version)
        {
            Filas[fila.IdProyecto] = fila;
        }
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<FilaPrograma>> IProyeccionPrograma.DeDesastreAsync(Guid idDesastre, CancellationToken ct)
    {
        IReadOnlyList<FilaPrograma> lista = Filas.Values.Where(f => f.IdDesastre == idDesastre).ToList();
        return Task.FromResult(lista);
    }

    public GuardadoDeProyectos Guardado() => new(this, this, this, this);

    /// <summary>Guarda directamente un proyecto ya armado, como si viniera de una operación anterior.</summary>
    public void Sembrar(ProyectoReconstruccion proyecto)
    {
        Historial.AddRange(proyecto.EntradasNuevas);
        proyecto.LimpiarPendientes();
        Proyectos[proyecto.Id] = Copiar(proyecto.AInstantanea());
    }

    private static InstantaneaProyecto Copiar(InstantaneaProyecto d)
    {
        // Copia las evaluaciones para que un proyecto cargado no comparta objetos con lo guardado.
        List<EvaluacionOrigen> evaluaciones = d.Evaluaciones
            .Select(e => new EvaluacionOrigen(e.IdEvaluacion, e.IdActivo, e.TipoActivo, e.NivelDano, e.CodigoMunicipio, e.Vigente)).ToList();
        return d with { Evaluaciones = evaluaciones };
    }
}

/// <summary>Presenter de prueba: recuerda qué se le entregó.</summary>
public sealed class SalidaDePrueba<T> : IPuertoSalida<T>
{
    public T? Respuesta { get; private set; }
    public string Resultado { get; private set; } = "";
    public string? Codigo { get; private set; }
    public string? Mensaje { get; private set; }
    public IReadOnlyList<string> Detalles { get; private set; } = Array.Empty<string>();

    public void Exito(T respuesta) { Resultado = "exito"; Respuesta = respuesta; }
    public void NoEncontrado(string mensaje) { Resultado = "no_encontrado"; Mensaje = mensaje; }
    public void NoAutorizado(string mensaje) { Resultado = "no_autorizado"; Mensaje = mensaje; }
    public void ReglaIncumplida(string codigo, string mensaje, IReadOnlyList<string>? detalles = null)
    {
        Resultado = "regla"; Codigo = codigo; Mensaje = mensaje; Detalles = detalles ?? Array.Empty<string>();
    }
}
