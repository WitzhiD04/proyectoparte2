using Auditoria.Aplicacion;
using Auditoria.Aplicacion.Puertos;
using Auditoria.Dominio;

namespace Auditoria.Pruebas;

/// <summary>Puertos en memoria para probar los casos de uso sin base de datos.</summary>
public sealed class RegistroEnMemoria : IRegistroAuditoria, ISecuenciasRegistradas
{
    public List<EntradaAuditoria> Entradas { get; } = new();

    public Task<bool> ExisteAsync(Guid idEvento, CancellationToken ct = default) =>
        Task.FromResult(Entradas.Any(e => e.IdEvento == idEvento));

    public void Agregar(EntradaAuditoria entrada) => Entradas.Add(entrada);

    public Task<PaginaEntradas> ConsultarAsync(FiltroRegistro f, int pagina, int tamano, CancellationToken ct = default)
    {
        List<EntradaAuditoria> filtradas = Entradas
            .Where(e => f.Entidad is null || e.Entidad == f.Entidad)
            .Where(e => f.IdEntidad is null || e.IdEntidad == f.IdEntidad)
            .Where(e => f.Actor is null || e.Actor == f.Actor)
            .Where(e => f.Tipo is null || e.Tipo == f.Tipo)
            .Where(e => f.Desde is null || e.InstanteEmision >= f.Desde)
            .Where(e => f.Hasta is null || e.InstanteEmision <= f.Hasta)
            .OrderBy(e => e.InstanteEmision).ThenBy(e => e.Productor).ThenBy(e => e.SecuenciaProductor)
            .ToList();
        return Task.FromResult(new PaginaEntradas(filtradas.Skip((pagina - 1) * tamano).Take(tamano).ToList(), filtradas.Count));
    }

    public Task<IReadOnlyList<string>> ProductoresAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Entradas.Where(e => e.SecuenciaProductor is not null)
            .Select(e => e.Productor).Distinct().Order(StringComparer.Ordinal).ToList());

    public Task<IReadOnlyList<long>> MayoresQueAsync(string productor, long desde, int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<long>>(Entradas.Where(e => e.Productor == productor && e.SecuenciaProductor > desde)
            .Select(e => e.SecuenciaProductor!.Value).Order().Take(limite).ToList());

    public Task<IReadOnlySet<long>> RegistradasAsync(string productor, IReadOnlyCollection<long> secuencias, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<long>>(Entradas.Where(e => e.Productor == productor && e.SecuenciaProductor is not null
            && secuencias.Contains(e.SecuenciaProductor.Value)).Select(e => e.SecuenciaProductor!.Value).ToHashSet());
}

/// <summary>Guarda copias "persistidas" para que la prueba no comparta objetos con el caso de uso.</summary>
public sealed class SeguimientosEnMemoria : ISeguimientos
{
    private readonly Dictionary<string, (long Contiguo, long Recibido, long Hasta, DateTimeOffset? En)> _estados = new();
    private readonly Dictionary<(string, long), (DateTimeOffset Detectado, DateTimeOffset? Reportado, DateTimeOffset? Cubierto)> _huecos = new();

    public Task<SeguimientoProductor?> BuscarAsync(string productor, CancellationToken ct = default) =>
        Task.FromResult(_estados.ContainsKey(productor) ? Leer(productor) : null);

    public Task GuardarAsync(SeguimientoProductor s, IReadOnlyList<HuecoSecuencia> cambiados, CancellationToken ct = default)
    {
        _estados[s.Productor] = (s.MayorContiguo, s.MayorRecibido, s.RevisadoHasta, s.RevisadoEn);
        foreach (HuecoSecuencia h in cambiados)
        {
            _huecos[(h.Productor, h.Secuencia)] = (h.DetectadoEn, h.ReportadoEn, h.CubiertoEn);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SeguimientoProductor>> TodosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SeguimientoProductor>>(_estados.Keys.Select(Leer).ToList());

    public Task<IReadOnlyList<HuecoSecuencia>> ReportadosAsync(int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HuecoSecuencia>>(Todos().Where(h => h.Reportado).Take(limite).ToList());

    private IEnumerable<HuecoSecuencia> Todos() => _huecos.Select(kv =>
        HuecoSecuencia.Reconstituir(kv.Key.Item1, kv.Key.Item2, kv.Value.Detectado, kv.Value.Reportado, kv.Value.Cubierto));

    private SeguimientoProductor Leer(string productor)
    {
        var e = _estados[productor];
        return SeguimientoProductor.Reconstituir(productor, e.Contiguo, e.Recibido, e.Hasta, e.En,
            Todos().Where(h => h.Productor == productor && h.Abierto));
    }
}

public sealed class AvisosEnMemoria : IAvisosHuecos
{
    public List<HuecoSecuencia> Reportados { get; } = new();
    public void HuecoReportado(HuecoSecuencia hueco, TimeSpan plazo) => Reportados.Add(hueco);
}

public sealed class RelojFijo : IReloj
{
    public RelojFijo(DateTimeOffset ahora) { Ahora = ahora; }
    public DateTimeOffset Ahora { get; set; }
    public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;
}

public sealed class SalidaEnMemoria<T> : IPuertoSalida<T>
{
    public T? Respuesta { get; private set; }
    public string? NoAutorizadoMensaje { get; private set; }
    public string? Codigo { get; private set; }

    public void Exito(T respuesta) => Respuesta = respuesta;
    public void NoAutorizado(string mensaje) => NoAutorizadoMensaje = mensaje;
    public void ReglaIncumplida(string codigo, string mensaje) => Codigo = codigo;
}
