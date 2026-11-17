using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;

namespace ConsultaDanos.Pruebas;

/// <summary>
/// El almacén de proyecciones en memoria, con la misma semántica que el de PostGIS (validación protegida por versión,
/// conflictos que se completan en cualquier orden, consolidado por incrementos). Sirve para probar el caso de uso con
/// todos los órdenes de llegada posibles.
/// </summary>
public sealed class AlmacenEnMemoria : IAlmacenProyecciones
{
    public Dictionary<Guid, EstadoActivo> Activos { get; } = [];
    public Dictionary<Guid, (EstadoValidacion Estado, int Version)> Validaciones { get; } = [];
    public Dictionary<ClaveZona, int> Consolidado { get; } = [];
    public HashSet<(Guid, Guid, int, string)> Historial { get; } = [];
    public Dictionary<Guid, (ConflictoLeido Conflicto, DateTimeOffset? Detectado)> Conflictos { get; } = [];
    public Dictionary<Guid, DateTimeOffset> Desastres { get; } = [];
    public int Pendientes { get; private set; }
    public DateTimeOffset? MasViejo { get; private set; }

    public Task<EstadoActivo> BloquearActivoAsync(Guid idActivo, DateTimeOffset instante, CancellationToken ct)
    {
        if (!Activos.TryGetValue(idActivo, out EstadoActivo? a))
        {
            Activos[idActivo] = a = EstadoActivo.Vacio(idActivo);
        }
        return Task.FromResult(a);
    }

    public Task GuardarActivosAsync(IReadOnlyList<EstadoActivo> cambiados, IReadOnlyList<(ClaveZona Clave, int Delta)> deltas,
        IReadOnlyCollection<Guid> desastres, DateTimeOffset instante, CancellationToken ct)
    {
        foreach (EstadoActivo a in cambiados) Activos[a.IdActivo] = a;
        foreach ((ClaveZona clave, int delta) in deltas)
        {
            int nuevo = Consolidado.GetValueOrDefault(clave) + delta;
            if (nuevo < 0) throw new InvalidOperationException($"El consolidado de {clave} quedaría negativo.");
            Consolidado[clave] = nuevo;
        }
        foreach (Guid d in desastres) MarcarDesastre(d, instante);
        return Task.CompletedTask;
    }

    public Task<EstadoValidacion?> ValidacionDeAsync(Guid idEvaluacion, CancellationToken ct) =>
        Task.FromResult(Validaciones.TryGetValue(idEvaluacion, out var v) ? v.Estado : (EstadoValidacion?)null);

    public Task GuardarValidacionAsync(ValidacionCambiada h, CancellationToken ct)
    {
        Guid id = h.Contenido.IdEvaluacion;
        if (!Validaciones.TryGetValue(id, out var v) || v.Version < h.Contenido.VersionEvaluacion)
        {
            Validaciones[id] = (h.Estado, h.Contenido.VersionEvaluacion);
        }
        return Task.CompletedTask;
    }

    public Task RegistrarHistorialAsync(EntradaHistorial e, CancellationToken ct)
    {
        Historial.Add((e.IdActivo, e.IdEvaluacion, e.VersionEvaluacion, e.Origen));
        return Task.CompletedTask;
    }

    public Task BloquearBandejaAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<ConflictoLeido> GuardarConflictoDetectadoAsync(ConflictoDetectado h, CancellationToken ct)
    {
        ConflictoLeido c = Conflictos.TryGetValue(h.IdConflicto, out var previo)
            ? previo.Conflicto with { IdActivo = h.IdActivo }
            : new ConflictoLeido(h.IdConflicto, h.IdActivo, "PENDIENTE", null, null);
        Conflictos[h.IdConflicto] = (c, h.Instante);
        return Task.FromResult(c);
    }

    public Task<ConflictoLeido> GuardarConflictoResueltoAsync(ConflictoResuelto h, CancellationToken ct)
    {
        Conflictos.TryGetValue(h.IdConflicto, out var previo);
        ConflictoLeido c = new(h.IdConflicto, previo.Conflicto?.IdActivo, "RESUELTO", h.Decision, h.IdActivoVigente);
        Conflictos[h.IdConflicto] = (c, previo.Detectado);
        return Task.FromResult(c);
    }

    public Task RecalcularBandejaAsync(DateTimeOffset instante, CancellationToken ct)
    {
        var pendientes = Conflictos.Values.Where(c => c.Conflicto.Estado == "PENDIENTE").ToList();
        Pendientes = pendientes.Count;
        MasViejo = pendientes.Count == 0 ? null : pendientes.Min(c => c.Detectado);
        return Task.CompletedTask;
    }

    public Task<Guid?> DesastreDelActivoAsync(Guid idActivo, CancellationToken ct) =>
        Task.FromResult(Activos.TryGetValue(idActivo, out EstadoActivo? a) ? a.Contenido?.IdDesastre : null);

    public Task MarcarDesastreAsync(Guid desastre, DateTimeOffset instante, CancellationToken ct)
    {
        MarcarDesastre(desastre, instante);
        return Task.CompletedTask;
    }

    private void MarcarDesastre(Guid d, DateTimeOffset instante) =>
        Desastres[d] = Desastres.TryGetValue(d, out DateTimeOffset previo) && previo > instante ? previo : instante;

    public Task GuardarCapaAsync(CapaRecibida h, CancellationToken ct) => Task.CompletedTask;
    public Task GuardarTipoActivoAsync(TipoActivoActualizado h, CancellationToken ct) => Task.CompletedTask;
    public Task GuardarIntegracionAsync(EstadoIntegracion h, CancellationToken ct) => Task.CompletedTask;
}

public sealed class InvalidacionEnMemoria : IInvalidacionMapa
{
    public HashSet<Guid> Desastres { get; } = [];
    public bool Global { get; private set; }
    public void Programar(IReadOnlyCollection<Guid> desastres) => Desastres.UnionWith(desastres);
    public void ProgramarGlobal() => Global = true;
}

/// <summary>Arma los hechos de una evaluación como los traduce el adaptador de entrada.</summary>
public static class Hechos
{
    public static readonly Guid Desastre = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly DateTimeOffset T0 = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    public static ContenidoActivo Contenido(Guid idEvaluacion, NivelDano nivel, int versionEvaluacion, string municipio = "63001",
        string tipo = "VIVIENDA", Guid? desastre = null, string estado = "REGISTRADA") =>
        new(idEvaluacion, desastre ?? Desastre, tipo, nivel, municipio, 4.53, -75.68, 5, estado, versionEvaluacion);

    public static EvaluacionRegistrada Sincronizada(Guid activo, ContenidoActivo c, int versionActivo, int minuto, bool enConflicto = false) =>
        new(activo, c, versionActivo, enConflicto ? OrigenContenido.SincronizacionEnConflicto : OrigenContenido.Evaluacion,
            OrigenHistorial.Sincronizacion, enConflicto, "dispositivo-1", T0.AddMinutes(minuto));

    public static ValidacionCambiada Validacion(Guid activo, ContenidoActivo c, EstadoValidacion estado, int minuto) =>
        new(activo, c, estado, c.VersionEvaluacion, "ungrd.operador", T0.AddMinutes(minuto), T0.AddMinutes(minuto));

    /// <summary>Todos los órdenes posibles de una secuencia (para secuencias cortas).</summary>
    public static IEnumerable<IReadOnlyList<T>> Permutaciones<T>(IReadOnlyList<T> elementos)
    {
        if (elementos.Count <= 1)
        {
            yield return elementos;
            yield break;
        }
        for (int i = 0; i < elementos.Count; i++)
        {
            List<T> resto = elementos.Where((_, j) => j != i).ToList();
            foreach (IReadOnlyList<T> p in Permutaciones(resto))
            {
                yield return new[] { elementos[i] }.Concat(p).ToList();
            }
        }
    }
}
