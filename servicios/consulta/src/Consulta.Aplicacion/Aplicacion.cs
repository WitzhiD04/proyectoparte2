using Consulta.Dominio;

namespace Consulta.Aplicacion;

// ------------------------------------------------------------------ hechos que llegan del bus, ya traducidos

public sealed record DesastreRegistrado(Guid Id, string Tipo, string Nombre, string Descripcion, string Severidad, string Estado,
    string OrganismoRegistra, string Responsable, DateTimeOffset InstanteInicio, double Lat, double Lon, int Version, string Autor, DateTimeOffset Instante);
public sealed record SeveridadCambiada(Guid Id, string Anterior, string Nueva, int Version, string Autor, string Motivo, DateTimeOffset Instante);
public sealed record EstadoCambiado(Guid Id, string Anterior, string Nuevo, int Version, string Autor, DateTimeOffset Instante);
public sealed record ZonaActualizada(Guid Id, int Version, string? GeometriaGeoJson, IReadOnlyList<string> CodigosMunicipio, string Fuente,
    string Autor, DateTimeOffset Instante);
public sealed record OrganismosCambiados(Guid Id, string Responsable, IReadOnlyList<(string Organismo, string Responsabilidad)> Participantes,
    int Version, string Autor, DateTimeOffset Instante);
public sealed record EstadoIntegracion(string Sistema, string Anterior, string Nuevo, DateTimeOffset Instante, long MensajesEnCola);
public sealed record DivisionLeida(string Codigo, string Nombre, string? CodigoPadre, string GeometriaGeoJson);

// ------------------------------------------------------------------ lo que se consulta

/// <summary>Un dato con su marca de tiempo (AD-16, CON-6): el usuario distingue un dato vigente de uno conservado.</summary>
public sealed record ConMarca<T>(T Valor, DateTimeOffset? DatoAl);

public sealed record FilaTablero(Guid Id, string? Tipo, string? Nombre, string? Severidad, string? Estado, string? Responsable,
    int ZonaVersion, int Municipios, DateTimeOffset DatoAl);

public sealed record VistaDesastre(Guid Id, string? Tipo, string? Nombre, string? Descripcion, DateTimeOffset? InstanteInicio, double? Lat, double? Lon,
    ConMarca<string?> Severidad, ConMarca<string?> Estado, ConMarca<string?> Responsable,
    ConMarca<IReadOnlyList<(string Organismo, string Responsabilidad)>> Participantes, ConMarca<ZonaLeida?> Zona, DateTimeOffset DatoAl);

public sealed record RespuestaConsulta<T>(T Datos, DateTimeOffset? DatoAl, bool Degradado);

public sealed record IntegracionLeida(string Sistema, string Estado, DateTimeOffset Desde, long MensajesEnCola);

// ------------------------------------------------------------------ puertos

/// <summary>Proyecciones del modelo de lectura. Cada aplicación solo acepta una versión más nueva del aspecto que cambia.</summary>
public interface IProyecciones
{
    Task AplicarAsync(DesastreRegistrado h, CancellationToken ct);
    Task AplicarAsync(SeveridadCambiada h, CancellationToken ct);
    Task AplicarAsync(EstadoCambiado h, CancellationToken ct);
    Task AplicarAsync(ZonaActualizada h, CancellationToken ct);
    Task AplicarAsync(OrganismosCambiados h, CancellationToken ct);
    Task AplicarAsync(EstadoIntegracion h, CancellationToken ct);
    Task AplicarCapaAsync(IReadOnlyList<DivisionLeida> divisiones, int version, CancellationToken ct);
}

public interface ILecturas
{
    Task<IReadOnlyList<FilaTablero>> TableroAsync(string? estado, string? tipo, CancellationToken ct);
    Task<VistaDesastre?> DesastreAsync(Guid id, CancellationToken ct);
    /// <summary>Zonas vigentes de los desastres (activos o todos), con la geometría simplificada, para el mapa.</summary>
    Task<IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)>> MapaAsync(bool soloActivos, CancellationToken ct);
    Task<(IReadOnlyList<CambioEvolucion> Cambios, IReadOnlyList<ZonaLeida> Zonas)?> EvolucionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct);
}

/// <summary>
/// Caché de geometrías y respuestas del mapa (AD-17). Las claves llevan una versión que sube con cada evento que cambia el
/// mapa, así que una respuesta vieja nunca se sirve como vigente y expira sola. El último mapa armado se conserva aparte para
/// la degradación controlada si cae la base (CON-6).
/// </summary>
public interface ICacheConsulta
{
    Task<string?> LeerAsync(string clave, CancellationToken ct);
    Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct);
    Task<long> VersionMapaAsync(CancellationToken ct);
    Task InvalidarMapaAsync(CancellationToken ct);
}

// ------------------------------------------------------------------ casos de uso

/// <summary>Aplica al modelo de lectura un hecho que llegó del bus, y marca el mapa como cambiado (EV-05 invalida la caché, AD-17).</summary>
public sealed class ActualizarProyecciones
{
    private readonly IProyecciones _proyecciones;
    private readonly ICacheConsulta _cache;

    public ActualizarProyecciones(IProyecciones proyecciones, ICacheConsulta cache)
    {
        _proyecciones = proyecciones;
        _cache = cache;
    }

    public async Task AplicarAsync(Func<IProyecciones, Task> aplicar, bool cambiaElMapa, CancellationToken ct)
    {
        await aplicar(_proyecciones);
        if (cambiaElMapa)
        {
            // Si la caché no responde, el mapa se arma desde la base: no se pierde el evento por eso.
            try
            {
                await _cache.InvalidarMapaAsync(ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
            }
        }
    }
}

/// <summary>Serializa el mapa (GeoJSON FeatureCollection). Es un adaptador: el formato no es asunto de la aplicación.</summary>
public interface IFormatoMapa
{
    string Serializar(IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)> zonas, DateTimeOffset? datoAl, bool degradado);
    string MarcarDegradado(string mapa);
}

/// <summary>
/// Mapa de zonas afectadas (Cuadro 15: /v1/consulta/mapa, FP-02, RF-GD-16), servido desde la caché de geometrías (AD-17).
/// Si la caché no responde, se arma desde la base. Si la base no responde, se devuelve el último mapa conocido marcado como
/// degradado, con su marca de tiempo, en lugar de un error (CON-6, AD-16).
/// </summary>
public sealed class ConsultarMapa
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);
    private readonly ILecturas _lecturas;
    private readonly ICacheConsulta _cache;
    private readonly IFormatoMapa _formato;

    public ConsultarMapa(ILecturas lecturas, ICacheConsulta cache, IFormatoMapa formato)
    {
        _lecturas = lecturas;
        _cache = cache;
        _formato = formato;
    }

    public async Task<(string Mapa, bool Degradado)> EjecutarAsync(bool soloActivos, CancellationToken ct)
    {
        string filtro = soloActivos ? "activos" : "todos";
        string? clave = null;
        try
        {
            clave = $"consulta:mapa:v{await _cache.VersionMapaAsync(ct)}:{filtro}";
            if (await _cache.LeerAsync(clave, ct) is { } enCache)
            {
                return (enCache, false);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            clave = null; // la caché no responde: se arma desde la base
        }

        try
        {
            IReadOnlyList<(FilaTablero Desastre, ZonaLeida Zona)> zonas = await _lecturas.MapaAsync(soloActivos, ct);
            DateTimeOffset? datoAl = zonas.Count == 0 ? null : zonas.Max(z => z.Desastre.DatoAl);
            string mapa = _formato.Serializar(zonas, datoAl, degradado: false);
            if (clave is not null)
            {
                await _cache.GuardarAsync(clave, mapa, Vigencia, ct);
                await _cache.GuardarAsync($"consulta:mapa:ultimo:{filtro}", mapa, null, ct);
            }
            return (mapa, false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            string? ultimo = await LeerSinFallarAsync($"consulta:mapa:ultimo:{filtro}", ct);
            if (ultimo is null)
            {
                throw;
            }
            return (_formato.MarcarDegradado(ultimo), true);
        }
    }

    private async Task<string?> LeerSinFallarAsync(string clave, CancellationToken ct)
    {
        try
        {
            return await _cache.LeerAsync(clave, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

public sealed class ConsultarDesastres
{
    private readonly ILecturas _lecturas;
    public ConsultarDesastres(ILecturas lecturas) { _lecturas = lecturas; }

    public async Task<RespuestaConsulta<IReadOnlyList<FilaTablero>>> TableroAsync(string? estado, string? tipo, CancellationToken ct)
    {
        IReadOnlyList<FilaTablero> filas = await _lecturas.TableroAsync(estado, tipo, ct);
        return new(filas, filas.Count == 0 ? null : filas.Max(f => f.DatoAl), false);
    }

    public Task<VistaDesastre?> DesastreAsync(Guid id, CancellationToken ct) => _lecturas.DesastreAsync(id, ct);

    public async Task<(IReadOnlyList<CambioEvolucion> Cambios, IReadOnlyList<ZonaLeida> Zonas, EstadoEnInstante? EnInstante)?> EvolucionAsync(
        Guid id, DateTimeOffset? en, CancellationToken ct)
    {
        var evolucion = await _lecturas.EvolucionAsync(id, ct);
        if (evolucion is null)
        {
            return null;
        }
        EstadoEnInstante? enInstante = en is null ? null : Evolucion.En(en.Value, evolucion.Value.Cambios, evolucion.Value.Zonas);
        return (evolucion.Value.Cambios, evolucion.Value.Zonas, enInstante);
    }

    public Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct) => _lecturas.IntegracionesAsync(ct);
}
