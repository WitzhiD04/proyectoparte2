namespace Auditoria.Dominio;

/// <summary>
/// Un número de la secuencia de un productor que no ha llegado (mitigación de ATAM R-02, E-18, E-31). Se detecta
/// cuando llega un número mayor. Solo se <b>reporta</b> si sigue faltando después del plazo, porque dos réplicas del
/// relevo pueden publicar fuera de orden y el bus puede reentregar tarde: un hueco breve no es una pérdida.
/// </summary>
public sealed class HuecoSecuencia
{
    private HuecoSecuencia(string productor, long secuencia, DateTimeOffset detectadoEn)
    {
        Productor = productor;
        Secuencia = secuencia;
        DetectadoEn = detectadoEn;
    }

    public string Productor { get; }
    public long Secuencia { get; }
    /// <summary>Primera revisión que vio el número faltante. El plazo se cuenta desde aquí.</summary>
    public DateTimeOffset DetectadoEn { get; }
    /// <summary>Cuándo se reportó (alerta HUECO_DETECTADO). Nulo mientras está dentro del plazo.</summary>
    public DateTimeOffset? ReportadoEn { get; private set; }
    /// <summary>Cuándo llegó por fin el evento. Un hueco reportado y cubierto se conserva: es parte del rastro.</summary>
    public DateTimeOffset? CubiertoEn { get; private set; }

    public bool Abierto => CubiertoEn is null;
    public bool Reportado => ReportadoEn is not null;

    internal static HuecoSecuencia Detectar(string productor, long secuencia, DateTimeOffset ahora) => new(productor, secuencia, ahora);

    public static HuecoSecuencia Reconstituir(string productor, long secuencia, DateTimeOffset detectadoEn,
        DateTimeOffset? reportadoEn, DateTimeOffset? cubiertoEn) =>
        new(productor, secuencia, detectadoEn) { ReportadoEn = reportadoEn, CubiertoEn = cubiertoEn };

    internal void Reportar(DateTimeOffset ahora) => ReportadoEn ??= ahora;

    internal void Cubrir(DateTimeOffset ahora) => CubiertoEn ??= ahora;
}

/// <summary>Lo que cambió en una revisión: cada lista se guarda y los reportados generan la alerta.</summary>
public sealed record RevisionSecuencia(
    IReadOnlyList<HuecoSecuencia> Detectados,
    IReadOnlyList<HuecoSecuencia> Cubiertos,
    IReadOnlyList<HuecoSecuencia> Reportados)
{
    /// <summary>Huecos nuevos o modificados, sin repetir (un hueco puede detectarse y reportarse en la misma revisión).</summary>
    public IReadOnlyList<HuecoSecuencia> Cambiados =>
        Detectados.Concat(Cubiertos).Concat(Reportados).Distinct().OrderBy(h => h.Secuencia).ToList();
}

/// <summary>
/// Estado de la secuencia de un productor visto desde Auditoría: el mayor número contiguo recibido, el mayor recibido y
/// los números que faltan entre ellos (R-02). Se alimenta de los números ya registrados, no del orden de llegada.
/// </summary>
public sealed class SeguimientoProductor
{
    /// <summary>Límite de huecos nuevos por revisión. Un salto enorme (por ejemplo, un contador corrupto) se procesa
    /// por tramos en revisiones sucesivas, sin agotar la memoria del servicio.</summary>
    public const int MaximoHuecosPorRevision = 10_000;

    private readonly SortedDictionary<long, HuecoSecuencia> _abiertos;

    private SeguimientoProductor(string productor, long mayorContiguo, long mayorRecibido, long revisadoHasta,
        DateTimeOffset? revisadoEn, IEnumerable<HuecoSecuencia> abiertos)
    {
        if (string.IsNullOrWhiteSpace(productor))
        {
            throw new ReglaDeNegocioException("PRODUCTOR_VACIO", "El seguimiento necesita el nombre del productor.");
        }
        Productor = productor;
        MayorContiguo = mayorContiguo;
        MayorRecibido = mayorRecibido;
        RevisadoHasta = revisadoHasta;
        RevisadoEn = revisadoEn;
        _abiertos = new SortedDictionary<long, HuecoSecuencia>(abiertos.Where(h => h.Abierto).ToDictionary(h => h.Secuencia));
    }

    public string Productor { get; }
    /// <summary>Todos los números del 1 a este llegaron.</summary>
    public long MayorContiguo { get; private set; }
    public long MayorRecibido { get; private set; }
    /// <summary>Hasta dónde se revisó la secuencia. Igual al mayor recibido, salvo mientras se procesa un salto enorme.</summary>
    public long RevisadoHasta { get; private set; }
    public DateTimeOffset? RevisadoEn { get; private set; }

    /// <summary>Números que faltan, en orden, reportados o todavía dentro del plazo.</summary>
    public IReadOnlyCollection<HuecoSecuencia> HuecosAbiertos => _abiertos.Values;

    /// <summary>Un productor del que todavía no se ha revisado nada: la secuencia empieza en 1 (E-31).</summary>
    public static SeguimientoProductor Nuevo(string productor) => new(productor, 0, 0, 0, null, []);

    public static SeguimientoProductor Reconstituir(string productor, long mayorContiguo, long mayorRecibido,
        long revisadoHasta, DateTimeOffset? revisadoEn, IEnumerable<HuecoSecuencia> abiertos) =>
        new(productor, mayorContiguo, mayorRecibido, revisadoHasta, revisadoEn, abiertos);

    /// <summary>
    /// Revisa la secuencia con los números registrados desde la revisión anterior.
    /// </summary>
    /// <param name="recibidas">Números registrados mayores que <see cref="RevisadoHasta"/>, en cualquier orden.</param>
    /// <param name="llegadasDeHuecos">Cuáles de los huecos abiertos ya están registrados.</param>
    /// <param name="ahora">Instante de la revisión.</param>
    /// <param name="plazo">Un hueco se reporta cuando lleva abierto más que este plazo.</param>
    public RevisionSecuencia Revisar(IEnumerable<long> recibidas, IReadOnlySet<long> llegadasDeHuecos,
        DateTimeOffset ahora, TimeSpan plazo, int maximoHuecosNuevos = MaximoHuecosPorRevision)
    {
        if (plazo < TimeSpan.Zero)
        {
            throw new ReglaDeNegocioException("PLAZO_INVALIDO", "El plazo de un hueco no puede ser negativo.");
        }

        // 1. Los huecos cuyo evento ya llegó se cierran (aunque ya se hubieran reportado: se conserva cuándo llegó).
        List<HuecoSecuencia> cubiertos = new();
        foreach (HuecoSecuencia hueco in _abiertos.Values.Where(h => llegadasDeHuecos.Contains(h.Secuencia)).ToList())
        {
            hueco.Cubrir(ahora);
            _abiertos.Remove(hueco.Secuencia);
            cubiertos.Add(hueco);
        }

        // 2. Cada salto entre números consecutivos recibidos abre un hueco por número faltante.
        List<HuecoSecuencia> detectados = new();
        long frontera = RevisadoHasta;
        foreach (long numero in recibidas.Where(n => n > RevisadoHasta).Distinct().Order())
        {
            long faltan = numero - frontera - 1;
            long cabe = maximoHuecosNuevos - detectados.Count;
            if (faltan > cabe)
            {
                // Salto demasiado grande para una revisión: se abre lo que cabe y se continúa en la siguiente.
                for (long k = frontera + 1; k <= frontera + cabe; k++)
                {
                    detectados.Add(Abrir(k, ahora));
                }
                frontera += cabe;
                break;
            }
            for (long k = frontera + 1; k < numero; k++)
            {
                detectados.Add(Abrir(k, ahora));
            }
            frontera = numero;
            MayorRecibido = Math.Max(MayorRecibido, numero);
        }
        RevisadoHasta = frontera;

        // 3. Se reporta lo que sigue faltando después del plazo (R-02). Un hueco recién abierto nunca se reporta.
        List<HuecoSecuencia> reportados = new();
        foreach (HuecoSecuencia hueco in _abiertos.Values.Where(h => !h.Reportado && ahora - h.DetectadoEn > plazo))
        {
            hueco.Reportar(ahora);
            reportados.Add(hueco);
        }

        MayorContiguo = _abiertos.Count > 0 ? _abiertos.Keys.First() - 1 : RevisadoHasta;
        RevisadoEn = ahora;
        return new RevisionSecuencia(detectados, cubiertos, reportados);
    }

    private HuecoSecuencia Abrir(long secuencia, DateTimeOffset ahora)
    {
        HuecoSecuencia hueco = HuecoSecuencia.Detectar(Productor, secuencia, ahora);
        _abiertos[secuencia] = hueco;
        return hueco;
    }
}
