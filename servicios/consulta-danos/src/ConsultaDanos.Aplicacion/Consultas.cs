using ConsultaDanos.Dominio;

namespace ConsultaDanos.Aplicacion;

/// <summary>
/// Una lectura servida desde la caché (AD-17) con degradación controlada (CON-6, AD-16):
/// <list type="number">
/// <item>Si la respuesta de la versión vigente está en la caché, se sirve.</item>
/// <item>Si no, o si la caché no responde, se arma desde las proyecciones y se guarda (con TTL corto) y como "última".</item>
/// <item>Si las proyecciones no responden, se sirve la última respuesta conocida marcada como degradada, con su
/// <c>datoAl</c>, en lugar de un error. Si tampoco hay, la ruta responde 503.</item>
/// </list>
/// </summary>
internal static class LecturaConCache
{
    public static async Task<RespuestaLectura> EjecutarAsync(ICacheDanos cache, IFormatoDanos formato, string recurso, Guid desastre,
        string filtro, TimeSpan vigencia, Func<bool, Task<string>> armar, CancellationToken ct)
    {
        string ultima = $"{recurso}:{desastre}:ultimo:{filtro}";
        string? clave = null;
        try
        {
            (long global, long version) = await cache.VersionesAsync(desastre, ct);
            clave = $"{recurso}:{desastre}:g{global}.v{version}:{filtro}";
            if (await cache.LeerAsync(clave, ct) is { } enCache)
            {
                return new RespuestaLectura(enCache, false);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            clave = null; // la caché no responde: se arma desde la base, más lento pero correcto
        }

        string respuesta;
        try
        {
            respuesta = await armar(clave is not null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            string? anterior = await LeerSinFallarAsync(cache, ultima, ct);
            return anterior is null
                ? throw new DatosNoDisponiblesException("Ni la base de consulta ni la caché tienen con qué responder.", e)
                : new RespuestaLectura(formato.MarcarDegradado(anterior), true);
        }

        if (clave is not null)
        {
            try
            {
                await cache.GuardarAsync(clave, respuesta, vigencia, ct);
                await cache.GuardarAsync(ultima, respuesta, null, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // La respuesta ya está armada y es correcta: que la caché falle al guardarla no la invalida.
            }
        }
        return new RespuestaLectura(respuesta, false);
    }

    public static async Task<string?> LeerSinFallarAsync(ICacheDanos cache, string clave, CancellationToken ct)
    {
        try
        {
            return await cache.LeerAsync(clave, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>
/// Mapa de daños de un desastre (RF-ED-11, Cuadro 15: /v1/consulta-danos/mapa): la coropleta por municipio con el índice
/// de daño y la distribución por nivel y, si se piden, los activos como puntos. Las geometrías simplificadas salen de la
/// caché por capa y versión (<c>geo:{capa}:{version}:{codigo}</c>, DA-08).
/// </summary>
public sealed class ConsultarMapa
{
    public const string Capa = "MUNICIPIOS";
    private static readonly TimeSpan VigenciaGeometria = TimeSpan.FromHours(24);
    private readonly ILecturasDanos _lecturas;
    private readonly ICacheDanos _cache;
    private readonly IFormatoDanos _formato;
    private readonly ParametrosConsulta _parametros;

    public ConsultarMapa(ILecturasDanos lecturas, ICacheDanos cache, IFormatoDanos formato, ParametrosConsulta parametros)
    {
        _lecturas = lecturas;
        _cache = cache;
        _formato = formato;
        _parametros = parametros;
    }

    public Task<RespuestaLectura> EjecutarAsync(FiltroMapa filtro, CancellationToken ct) =>
        LecturaConCache.EjecutarAsync(_cache, _formato, "mapa", filtro.Desastre, filtro.Clave, _parametros.VigenciaRespuesta,
            usarCache => ArmarAsync(filtro, usarCache, ct), ct);

    private async Task<string> ArmarAsync(FiltroMapa filtro, bool usarCache, CancellationToken ct)
    {
        IReadOnlyList<ZonaMapa> zonas = await _lecturas.ZonasMapaAsync(filtro, ct);
        IReadOnlyList<PuntoMapa> puntos = filtro.Puntos ? await _lecturas.PuntosAsync(filtro, ct) : [];
        IReadOnlyDictionary<string, string> geometrias = await GeometriasAsync(zonas, usarCache, ct);
        DateTimeOffset? datoAl = Max(await _lecturas.DatoAlAsync(filtro.Desastre, ct), zonas.Select(z => z.CapaAl).Max());
        return _formato.Mapa(filtro, zonas, geometrias, puntos, datoAl);
    }

    /// <summary>Las geometrías de la caché; las que faltan se leen de la base y se guardan para la próxima vez (AD-17).</summary>
    private async Task<IReadOnlyDictionary<string, string>> GeometriasAsync(IReadOnlyList<ZonaMapa> zonas, bool usarCache, CancellationToken ct)
    {
        List<ZonaMapa> conCapa = zonas.Where(z => z.VersionCapa > 0).ToList();
        Dictionary<string, string> geometrias = new(StringComparer.Ordinal);
        if (usarCache && conCapa.Count > 0)
        {
            try
            {
                IReadOnlyList<string?> valores = await _cache.LeerVariasAsync(conCapa.Select(ClaveGeo).ToList(), ct);
                for (int i = 0; i < conCapa.Count; i++)
                {
                    if (valores[i] is { } g) geometrias[conCapa[i].Codigo] = g;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                usarCache = false;
            }
        }

        List<ZonaMapa> faltantes = conCapa.Where(z => !geometrias.ContainsKey(z.Codigo)).ToList();
        if (faltantes.Count == 0)
        {
            return geometrias;
        }
        IReadOnlyDictionary<string, string> deLaBase = await _lecturas.GeometriasAsync(faltantes.Select(z => z.Codigo).ToList(), ct);
        foreach (ZonaMapa z in faltantes)
        {
            if (!deLaBase.TryGetValue(z.Codigo, out string? g)) continue;
            geometrias[z.Codigo] = g;
            if (!usarCache) continue;
            try
            {
                await _cache.GuardarAsync(ClaveGeo(z), g, VigenciaGeometria, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                usarCache = false;
            }
        }
        return geometrias;
    }

    private static string ClaveGeo(ZonaMapa z) => $"geo:{Capa}:{z.VersionCapa}:{z.Codigo}";

    internal static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : (a > b ? a : b);
}

/// <summary>
/// Consolidado del impacto por zona y por tipo de activo, con la priorización (RF-ED-10, DA-08). Solo cuentan las
/// evaluaciones validadas. Usa la misma caché y la misma degradación que el mapa, porque es parte del "resumen de daños"
/// de la consulta frecuente (QA-06).
/// </summary>
public sealed class ConsultarConsolidado
{
    private readonly ILecturasDanos _lecturas;
    private readonly ICacheDanos _cache;
    private readonly IFormatoDanos _formato;
    private readonly ParametrosConsulta _parametros;

    public ConsultarConsolidado(ILecturasDanos lecturas, ICacheDanos cache, IFormatoDanos formato, ParametrosConsulta parametros)
    {
        _lecturas = lecturas;
        _cache = cache;
        _formato = formato;
        _parametros = parametros;
    }

    public Task<RespuestaLectura> EjecutarAsync(Guid desastre, CancellationToken ct) =>
        LecturaConCache.EjecutarAsync(_cache, _formato, "consolidado", desastre, "todo", _parametros.VigenciaRespuesta,
            async _ => _formato.Consolidado(await CalcularAsync(desastre, ct)), ct);

    public async Task<Consolidado> CalcularAsync(Guid desastre, CancellationToken ct)
    {
        IReadOnlyList<ZonaConsolidada> zonas = await _lecturas.ConsolidadoAsync(desastre, ct);
        IReadOnlySet<string> criticos = _parametros.TiposCriticos;
        IReadOnlyList<ZonaPriorizada> priorizadas = Priorizacion.Ordenar(zonas, criticos);
        Distribucion total = priorizadas.Aggregate(new Distribucion(), (d, z) => d.Sumar(z.Total));
        List<ResumenTipo> porTipo = zonas.SelectMany(z => z.Conteos)
            .GroupBy(c => c.TipoActivo)
            .Select(g => new ResumenTipo(g.Key, g.Select(c => c.NombreTipo).FirstOrDefault(n => n is not null), criticos.Contains(g.Key),
                g.Aggregate(new Distribucion(), (d, c) => d.Sumar(c.Nivel, c.Cantidad))))
            .Where(t => t.Distribucion.Total > 0)
            .OrderByDescending(t => t.Critico).ThenBy(t => t.TipoActivo, StringComparer.Ordinal)
            .ToList();
        return new Consolidado(desastre, await _lecturas.DatoAlAsync(desastre, ct), total, priorizadas, porTipo, criticos,
            await _lecturas.IntegracionesAsync(ct));
    }
}

/// <summary>Evolución de un activo (RF-ED-15): la secuencia de sus evaluaciones en el tiempo, con su nivel de daño.</summary>
public sealed class ConsultarEvolucion
{
    private readonly ILecturasDanos _lecturas;
    public ConsultarEvolucion(ILecturasDanos lecturas) { _lecturas = lecturas; }

    public Task<EvolucionActivo?> EjecutarAsync(Guid idActivo, CancellationToken ct) => _lecturas.EvolucionAsync(idActivo, ct);
}

/// <summary>
/// Evolución de una zona (RF-ED-15, E-40): las evaluaciones registradas en un municipio o un departamento, en orden de
/// tiempo, y cuántos activos hubo en cada nivel por día. La zona se valida contra la capa EV-21, como en el mapa.
/// </summary>
public sealed class ConsultarEvolucionZona
{
    /// <summary>Colombia está todo el año en UTC-5: no tiene horario de verano.</summary>
    private static readonly TimeSpan HoraColombia = TimeSpan.FromHours(-5);

    private readonly ILecturasDanos _lecturas;
    public ConsultarEvolucionZona(ILecturasDanos lecturas) { _lecturas = lecturas; }

    /// <summary>Nulo si la zona no está en la capa. Si está y no tiene evaluaciones, la evolución viene sin entradas.</summary>
    /// <exception cref="ArgumentException">El código no es DIVIPOLA: la ruta lo revisa antes con <see cref="CodigoZona"/>.</exception>
    public async Task<EvolucionZona?> EjecutarAsync(string codigo, Guid? desastre, CancellationToken ct)
    {
        TipoZona tipo = CodigoZona.TipoDe(codigo) ?? throw new ArgumentException($"'{codigo}' no es un código DIVIPOLA.", nameof(codigo));
        try
        {
            if (await ZonaAsync(codigo, tipo, ct) is not { } zona)
            {
                return null;
            }
            IReadOnlyList<EntradaHistorial> entradas = await _lecturas.HistorialZonaAsync(codigo, tipo, desastre, ct);
            return new EvolucionZona(zona, entradas.Count == 0 ? null : entradas.Max(e => e.Instante), entradas, PorDia(entradas));
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            throw new DatosNoDisponiblesException("La base de consulta no responde.", e);
        }
    }

    private async Task<ZonaConsultada?> ZonaAsync(string codigo, TipoZona tipo, CancellationToken ct)
    {
        if (tipo == TipoZona.DEPARTAMENTO)
        {
            return await _lecturas.DepartamentoEnCapaAsync(codigo, ct)
                ? new ZonaConsultada(codigo, tipo, Departamentos.NombreDe(codigo))
                : null;
        }
        return await _lecturas.NombreMunicipioAsync(codigo, ct) is { } nombre ? new ZonaConsultada(codigo, tipo, nombre) : null;
    }

    /// <summary>
    /// Por cada día en hora de Colombia, los activos con alguna entrada ese día, cada uno una sola vez y en el último nivel que tuvo. Una
    /// evaluación rechazada hasta ese día no cuenta: igual que en el mapa, no describe el daño del activo.
    /// </summary>
    private static List<ConteoDia> PorDia(IReadOnlyList<EntradaHistorial> entradas)
    {
        Dictionary<Guid, EstadoValidacion> validaciones = [];
        List<ConteoDia> dias = [];
        foreach (IGrouping<DateOnly, EntradaHistorial> dia in entradas.GroupBy(e => DateOnly.FromDateTime(e.Instante.ToOffset(HoraColombia).DateTime)).OrderBy(g => g.Key))
        {
            foreach (EntradaHistorial e in dia.Where(e => e.Validacion is not null))
            {
                validaciones[e.IdEvaluacion] = e.Validacion!.Value;
            }
            Distribucion distribucion = new();
            foreach (IGrouping<Guid, EntradaHistorial> activo in dia.GroupBy(e => e.IdActivo))
            {
                EntradaHistorial? ultima = activo.LastOrDefault(e =>
                    !validaciones.TryGetValue(e.IdEvaluacion, out EstadoValidacion v) || v != EstadoValidacion.RECHAZADA);
                if (ultima is not null)
                {
                    distribucion.Sumar(ultima.Nivel, 1);
                }
            }
            dias.Add(new ConteoDia(dia.Key, distribucion));
        }
        return dias;
    }
}

/// <summary>Indicadores de revisión y alarma de R-08 (DA-09, E-09): se leen de <c>p_bandeja</c> y se comparan con los umbrales.</summary>
public sealed class ConsultarIndicadores
{
    private readonly ILecturasDanos _lecturas;
    private readonly TimeProvider _reloj;
    private readonly ParametrosConsulta _parametros;

    public ConsultarIndicadores(ILecturasDanos lecturas, TimeProvider reloj, ParametrosConsulta parametros)
    {
        _lecturas = lecturas;
        _reloj = reloj;
        _parametros = parametros;
    }

    public async Task<(IndicadoresRevision Indicadores, DateTimeOffset? DatoAl)> EjecutarAsync(CancellationToken ct)
    {
        BandejaLeida bandeja = await _lecturas.BandejaAsync(ct);
        return (IndicadoresRevision.Calcular(bandeja.Pendientes, bandeja.MasViejo, _reloj.GetUtcNow(), _parametros.Umbrales), bandeja.DatoAl);
    }
}
