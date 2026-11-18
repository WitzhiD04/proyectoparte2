namespace SimSe6;

public sealed class OpcionesSimulador
{
    /// <summary>Retraso de cada respuesta en modo "lento" (mayor que el tiempo de espera del adaptador).</summary>
    public int RetrasoLentoMs { get; set; } = 10_000;
}

/// <summary>
/// Estado de SE-6 en memoria: cuentas de proyecto con sus movimientos financieros registrados por lotes (Cuadro 14,
/// DA-11). La idempotencia se aplica por elemento con el par (<c>codigoProyecto</c>, <c>versionHistorial</c>).
/// </summary>
public sealed class RegistroSe6
{
    private sealed class CuentaProyecto
    {
        public required string Codigo { get; init; }
        public string Titulo { get; set; } = "";
        public string CodigoDesastre { get; set; } = "";
        public string Situacion { get; set; } = Situaciones.Abierta;
        public decimal? Presupuesto { get; set; }
        public string? FuenteFinanciacion { get; set; }
        public decimal CostoAcumulado { get; set; }
        public SortedSet<int> Versiones { get; } = [];
        public SortedDictionary<int, MovimientoFinanciero> MovimientosPorVersion { get; } = [];
        public int Repetidas { get; set; }

        public void RecalcularEstado()
        {
            foreach (MovimientoFinanciero m in MovimientosPorVersion.Values)
            {
                Titulo = m.TituloProyecto;
                CodigoDesastre = m.CodigoDesastre;
                Situacion = m.Situacion;
                CostoAcumulado = m.CostoAcumulado;
                if (m.Presupuesto is not null)
                {
                    Presupuesto = m.Presupuesto;
                }
                if (m.FuenteFinanciacion is not null)
                {
                    FuenteFinanciacion = m.FuenteFinanciacion;
                }
            }
        }
    }

    private readonly object _candado = new();
    private readonly Dictionary<string, CuentaProyecto> _cuentas = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MovimientoFinanciero> _movimientos = [];
    private int _lotesRecibidos;

    public string Modo { get; private set; } = "off";

    public void CambiarModo(string modo)
    {
        if (modo is not ("on" or "off" or "lento"))
        {
            throw new ArgumentException("El modo es on, off o lento.", nameof(modo));
        }
        Modo = modo;
    }

    public static bool EsMovimientoValido(MovimientoFinanciero m)
    {
        if (string.IsNullOrWhiteSpace(m.CodigoProyecto)
            || m.VersionHistorial < 1
            || string.IsNullOrWhiteSpace(m.TituloProyecto)
            || string.IsNullOrWhiteSpace(m.CodigoDesastre)
            || !ClasesMovimiento.Todas.Contains(m.Clase)
            || !Situaciones.Todas.Contains(m.Situacion)
            || m.CostoAcumulado < 0)
        {
            return false;
        }
        if (m.Clase == ClasesMovimiento.Costo && m.MontoMovimiento is null or <= 0)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Registra un lote de movimientos de forma idempotente por elemento (<c>codigoProyecto</c> + <c>versionHistorial</c>).
    /// </summary>
    public RespuestaLote RegistrarLote(string idLote, IReadOnlyList<MovimientoFinanciero> movimientos)
    {
        lock (_candado)
        {
            _lotesRecibidos++;
            int nuevos = 0, repetidos = 0;
            List<ResultadoElementoLote> resultados = new(movimientos.Count);

            foreach (MovimientoFinanciero m in movimientos)
            {
                if (!_cuentas.TryGetValue(m.CodigoProyecto, out CuentaProyecto? cuenta))
                {
                    cuenta = new CuentaProyecto { Codigo = m.CodigoProyecto };
                    _cuentas[m.CodigoProyecto] = cuenta;
                }

                if (!cuenta.Versiones.Add(m.VersionHistorial))
                {
                    cuenta.Repetidas++;
                    repetidos++;
                    resultados.Add(new ResultadoElementoLote(m.CodigoProyecto, m.VersionHistorial, false));
                    continue;
                }

                cuenta.MovimientosPorVersion[m.VersionHistorial] = m;
                cuenta.RecalcularEstado();
                _movimientos.Add(m);
                nuevos++;
                resultados.Add(new ResultadoElementoLote(m.CodigoProyecto, m.VersionHistorial, true));
            }

            return new RespuestaLote(idLote, movimientos.Count, nuevos, repetidos, resultados);
        }
    }

    public IReadOnlyList<MovimientoFinanciero> Movimientos(string? codigoProyecto = null)
    {
        lock (_candado)
        {
            if (string.IsNullOrWhiteSpace(codigoProyecto))
            {
                return _movimientos.ToList();
            }
            return _cuentas.TryGetValue(codigoProyecto, out CuentaProyecto? cuenta)
                ? cuenta.MovimientosPorVersion.Values.ToList()
                : [];
        }
    }

    public EstadoCuentaProyecto? Proyecto(string codigoProyecto)
    {
        lock (_candado)
        {
            return _cuentas.TryGetValue(codigoProyecto, out CuentaProyecto? c) ? AEstado(c) : null;
        }
    }

    public EstadoSimulador Estado()
    {
        lock (_candado)
        {
            List<EstadoCuentaProyecto> proyectos = _cuentas.Values
                .OrderBy(c => c.Codigo, StringComparer.Ordinal)
                .Select(AEstado)
                .ToList();
            return new EstadoSimulador(
                Modo,
                proyectos,
                _movimientos.ToList(),
                _lotesRecibidos,
                _movimientos.Count,
                proyectos.Sum(p => p.EntregasRepetidas));
        }
    }

    private static EstadoCuentaProyecto AEstado(CuentaProyecto c) => new(
        c.Codigo,
        c.Titulo,
        c.CodigoDesastre,
        c.Situacion,
        c.Presupuesto,
        c.FuenteFinanciacion,
        c.CostoAcumulado,
        c.Versiones.ToList(),
        c.MovimientosPorVersion.Values.ToList(),
        c.Repetidas);
}
