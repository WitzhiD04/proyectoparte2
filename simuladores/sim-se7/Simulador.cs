namespace SimSe7;

public sealed class OpcionesSimulador
{
    /// <summary>Cada cuánto SE-7 asigna contratistas y avanza las obras. 0: no genera nada solo (pruebas).</summary>
    public double IntervaloGeneracionSegundos { get; set; } = 20;

    /// <summary>Retraso de cada respuesta en modo "lento" (mayor que el tiempo de espera del adaptador).</summary>
    public int RetrasoLentoMs { get; set; } = 10_000;

    /// <summary>Tamaño máximo de una página de cambios.</summary>
    public int LimitePagina { get; set; } = 100;
}

/// <summary>
/// Estado de SE-7 en memoria: obras con sus versiones registradas y un registro de cambios de solo inserción que se
/// consulta desde una marca (la secuencia). La consulta es idempotente: la misma marca devuelve los mismos cambios.
/// </summary>
public sealed class RegistroSe7
{
    private sealed class Obra
    {
        public required string Codigo { get; init; }
        public string Titulo { get; set; } = "";
        public string Situacion { get; set; } = Situaciones.Formulada;
        public decimal Avance { get; set; }
        public string? Contratista { get; set; }
        public SortedSet<int> Versiones { get; } = [];
        public int Repetidas { get; set; }
    }

    private readonly object _candado = new();
    private readonly Dictionary<string, Obra> _obras = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CambioObra> _cambios = [];
    private readonly TimeProvider _reloj;
    private int _contratistas;

    public RegistroSe7(TimeProvider? reloj = null)
    {
        _reloj = reloj ?? TimeProvider.System;
    }

    public string Modo { get; private set; } = "off";

    public void CambiarModo(string modo)
    {
        if (modo is not ("on" or "off" or "lento"))
        {
            throw new ArgumentException("El modo es on, off o lento.", nameof(modo));
        }
        Modo = modo;
    }

    /// <summary>Registra una versión de una obra. Devuelve falso si ya la tenía (idempotencia por obra y versión).</summary>
    public bool RegistrarVersion(string codigo, int version, VersionObra datos)
    {
        lock (_candado)
        {
            if (!_obras.TryGetValue(codigo, out Obra? obra))
            {
                // Una modificación puede llegar antes que el alta si la plataforma entrega en paralelo: SE-7 la acepta.
                obra = new Obra { Codigo = codigo };
                _obras[codigo] = obra;
            }
            if (!obra.Versiones.Add(version))
            {
                obra.Repetidas++;
                return false;
            }
            // Solo la versión más nueva define el estado actual de la obra.
            if (version == obra.Versiones.Max)
            {
                obra.Titulo = datos.Titulo;
                obra.Situacion = datos.Situacion;
                obra.Avance = datos.PorcentajeAvance;
                if (datos.Contratista is not null)
                {
                    obra.Contratista = datos.Contratista;
                }
                else if (datos.Concepto == "CONTRATISTA" && datos.ValorNuevo is not null)
                {
                    obra.Contratista = datos.ValorNuevo;
                }
            }
            // Lo que llega de la plataforma no se reporta de vuelta como cambio: SE-7 solo reporta lo que nace en él.
            return true;
        }
    }

    public PaginaCambios CambiosDesde(long desde, int limite)
    {
        lock (_candado)
        {
            // La secuencia empieza en 1 y no tiene huecos: la posición en la lista es secuencia - 1.
            int inicio = (int)Math.Clamp(desde, 0, _cambios.Count);
            List<CambioObra> pagina = _cambios.Skip(inicio).Take(limite).ToList();
            string? marca = pagina.Count > 0 ? pagina[^1].Secuencia.ToString() : (desde > 0 ? desde.ToString() : null);
            return new PaginaCambios(pagina, marca, inicio + pagina.Count < _cambios.Count);
        }
    }

    public CambioObra AgregarCambio(string codigo, string clase, decimal? avance, string? contratista)
    {
        lock (_candado)
        {
            if (clase is not ("AVANCE" or "CONTRATISTA"))
            {
                throw new ArgumentException("La clase es AVANCE o CONTRATISTA.", nameof(clase));
            }
            if (clase == "AVANCE" && avance is not (>= 0 and <= 100))
            {
                throw new ArgumentException("El avance va de 0 a 100.", nameof(avance));
            }
            if (clase == "CONTRATISTA" && string.IsNullOrWhiteSpace(contratista))
            {
                throw new ArgumentException("Falta el contratista.", nameof(contratista));
            }
            if (_obras.TryGetValue(codigo, out Obra? obra))
            {
                if (clase == "AVANCE")
                {
                    obra.Avance = avance!.Value;
                }
                else
                {
                    obra.Contratista = contratista;
                }
            }
            CambioObra cambio = new(_cambios.Count + 1, codigo, clase, clase == "AVANCE" ? avance : null,
                clase == "CONTRATISTA" ? contratista : null, _reloj.GetUtcNow());
            _cambios.Add(cambio);
            return cambio;
        }
    }

    /// <summary>
    /// Lo que pasa en la obra con el tiempo: una obra aprobada sin contratista recibe uno; una obra en ejecución avanza
    /// entre 5 y 15 puntos hasta 100. Devuelve los cambios generados.
    /// </summary>
    public IReadOnlyList<CambioObra> Generar(Random azar)
    {
        lock (_candado)
        {
            List<CambioObra> generados = [];
            foreach (Obra obra in _obras.Values.OrderBy(o => o.Codigo, StringComparer.Ordinal))
            {
                if (obra.Situacion == Situaciones.Aprobada && obra.Contratista is null)
                {
                    generados.Add(AgregarCambio(obra.Codigo, "CONTRATISTA", null, $"Constructora {++_contratistas:000} S.A.S."));
                }
                else if (obra.Situacion == Situaciones.EnObra && obra.Avance < 100)
                {
                    decimal nuevo = Math.Min(100, obra.Avance + azar.Next(5, 16));
                    generados.Add(AgregarCambio(obra.Codigo, "AVANCE", nuevo, null));
                }
            }
            return generados;
        }
    }

    public EstadoSimulador Estado()
    {
        lock (_candado)
        {
            List<EstadoObra> obras = _obras.Values.OrderBy(o => o.Codigo, StringComparer.Ordinal)
                .Select(o => new EstadoObra(o.Codigo, o.Titulo, o.Situacion, o.Avance, o.Contratista, o.Versiones.ToList(), o.Repetidas))
                .ToList();
            return new EstadoSimulador(Modo, obras, _cambios.Count, obras.Sum(o => o.Versiones.Count), obras.Sum(o => o.EntregasRepetidas));
        }
    }
}

/// <summary>Genera avances y contratistas cada cierto tiempo, como si las obras avanzaran.</summary>
public sealed class GeneradorCambios(RegistroSe7 registro, OpcionesSimulador opciones, ILogger<GeneradorCambios> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (opciones.IntervaloGeneracionSegundos <= 0)
        {
            return;
        }
        Random azar = new();
        using PeriodicTimer reloj = new(TimeSpan.FromSeconds(opciones.IntervaloGeneracionSegundos));
        while (await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            IReadOnlyList<CambioObra> generados = registro.Generar(azar);
            if (generados.Count > 0)
            {
                log.LogInformation("SE-7 generó {Cantidad} cambios (último {Secuencia}).", generados.Count, generados[^1].Secuencia);
            }
        }
    }
}
