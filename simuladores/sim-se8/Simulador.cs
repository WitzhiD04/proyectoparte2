using System.Net.Http.Json;
using System.Threading.Channels;

namespace SimSe8;

public sealed class OpcionesSimulador
{
    /// <summary>URL del aviso de acuse del adaptador (ruta de la red interna, no pasa por el gateway).</summary>
    public string UrlAcuse { get; set; } = "http://adaptador-se8:8080/v1/acuses";

    /// <summary>Tiempo entre recibir una alerta y acusarla (el canal la entrega).</summary>
    public int RetrasoAcuseMs { get; set; } = 300;

    /// <summary>Intentos del aviso de acuse al adaptador; después queda para la consulta por marca.</summary>
    public int IntentosAviso { get; set; } = 5;

    /// <summary>Retraso de cada respuesta en modo "lento" (mayor que el tiempo de espera del adaptador).</summary>
    public int RetrasoLentoMs { get; set; } = 10_000;

    public int LimitePagina { get; set; } = 100;
}

/// <summary>
/// Estado de SE-8 en memoria. Una alerta se registra una vez por referencia (idempotencia): repetirla no produce un
/// segundo envío al canal. Cada alerta nueva produce un acuse, que se avisa al adaptador y queda en un registro de solo
/// inserción que se consulta desde una marca.
/// </summary>
public sealed class RegistroSe8
{
    private sealed class Registrada
    {
        public required AlertaSe8 Datos { get; init; }
        public int Recepciones { get; set; }
        public AcuseSe8? Acuse { get; set; }
    }

    private readonly object _candado = new();
    private readonly Dictionary<string, Registrada> _alertas = new(StringComparer.Ordinal);
    private readonly List<AcuseSe8> _acuses = [];
    private readonly Channel<string> _porAcusar = Channel.CreateUnbounded<string>();
    private readonly TimeProvider _reloj;
    private int _recepciones;
    private int _avisosFallidos;
    private int _avisosEntregados;

    public RegistroSe8(TimeProvider? reloj = null)
    {
        _reloj = reloj ?? TimeProvider.System;
    }

    public string Modo { get; private set; } = "off";

    public ChannelReader<string> PorAcusar => _porAcusar.Reader;

    public void CambiarModo(string modo)
    {
        if (modo is not ("on" or "off" or "lento" or "sin-acuse"))
        {
            throw new ArgumentException("El modo es on, off, lento o sin-acuse.", nameof(modo));
        }
        Modo = modo;
    }

    /// <summary>Registra una alerta. Devuelve falso si ya la tenía (misma referencia): no hay segundo envío.</summary>
    public bool Registrar(AlertaSe8 alerta)
    {
        lock (_candado)
        {
            _recepciones++;
            if (_alertas.TryGetValue(alerta.ReferenciaExterna!, out Registrada? existente))
            {
                existente.Recepciones++;
                // Un reenvío de una alerta que el canal aún no acusó vuelve a intentar el acuse; no es un envío nuevo.
                if (existente.Acuse is null && Modo != "sin-acuse")
                {
                    _porAcusar.Writer.TryWrite(alerta.ReferenciaExterna!);
                }
                return false;
            }
            _alertas[alerta.ReferenciaExterna!] = new Registrada { Datos = alerta, Recepciones = 1 };
        }
        if (Modo != "sin-acuse")
        {
            _porAcusar.Writer.TryWrite(alerta.ReferenciaExterna!);
        }
        return true;
    }

    /// <summary>El canal entregó (o rechazó) la alerta: se registra su acuse con la siguiente secuencia.</summary>
    public AcuseSe8? Acusar(string referencia)
    {
        lock (_candado)
        {
            if (!_alertas.TryGetValue(referencia, out Registrada? alerta) || alerta.Acuse is not null)
            {
                return alerta?.Acuse;
            }
            // Un destinatario con "rechazar" simula un canal que no acepta el destino.
            bool rechazada = alerta.Datos.Destinatario!.Contains("rechazar", StringComparison.OrdinalIgnoreCase);
            alerta.Acuse = new AcuseSe8(referencia, _acuses.Count + 1, rechazada ? "RECHAZADA" : "ENTREGADA",
                rechazada ? "El canal no acepta el destinatario" : null, _reloj.GetUtcNow());
            _acuses.Add(alerta.Acuse);
            return alerta.Acuse;
        }
    }

    /// <summary>Acuses después de una marca, en orden. La misma marca devuelve los mismos acuses.</summary>
    public PaginaAcuses AcusesDesde(long marca, int limite)
    {
        lock (_candado)
        {
            List<AcuseSe8> pagina = _acuses.Where(a => a.Secuencia > marca).Take(limite).ToList();
            bool hayMas = _acuses.Count(a => a.Secuencia > marca) > pagina.Count;
            return new PaginaAcuses(pagina, hayMas);
        }
    }

    public void AnotarAviso(bool entregado)
    {
        lock (_candado)
        {
            if (entregado)
            {
                _avisosEntregados++;
            }
            else
            {
                _avisosFallidos++;
            }
        }
    }

    public object? Alerta(string referencia)
    {
        lock (_candado)
        {
            return _alertas.TryGetValue(referencia, out Registrada? a)
                ? new { a.Datos, a.Recepciones, a.Acuse }
                : null;
        }
    }

    public object Estado()
    {
        lock (_candado)
        {
            return new
            {
                Modo,
                AlertasUnicas = _alertas.Count,
                Recepciones = _recepciones,
                Duplicadas = _alertas.Values.Count(a => a.Recepciones > 1),
                Acuses = _acuses.Count,
                AvisosEntregados = _avisosEntregados,
                AvisosFallidos = _avisosFallidos,
                Referencias = _alertas.ToDictionary(p => p.Key, p => p.Value.Recepciones),
            };
        }
    }

    public void Reiniciar()
    {
        lock (_candado)
        {
            _alertas.Clear();
            _acuses.Clear();
            _recepciones = 0;
            _avisosEntregados = 0;
            _avisosFallidos = 0;
            Modo = "off";
        }
    }
}

/// <summary>
/// Canal de entrega simulado: acusa cada alerta nueva después de un retraso y avisa al adaptador por la ruta interna.
/// Si el aviso falla (adaptador caído, alerta todavía sin confirmar en su base), lo reintenta; si se agotan los
/// intentos, el acuse queda en GET /v1/acuses para la conciliación por marca de avance.
/// </summary>
public sealed class CanalAcuses : BackgroundService
{
    private readonly RegistroSe8 _registro;
    private readonly OpcionesSimulador _opciones;
    private readonly HttpClient _http;
    private readonly ILogger<CanalAcuses> _log;

    public CanalAcuses(RegistroSe8 registro, OpcionesSimulador opciones, IHttpClientFactory fabrica, ILogger<CanalAcuses> log)
    {
        _registro = registro;
        _opciones = opciones;
        _http = fabrica.CreateClient("acuses");
        _http.Timeout = TimeSpan.FromSeconds(3);
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (string referencia in _registro.PorAcusar.ReadAllAsync(ct))
        {
            _ = Task.Run(() => AcusarAsync(referencia, ct), ct);
        }
    }

    private async Task AcusarAsync(string referencia, CancellationToken ct)
    {
        try
        {
            await Task.Delay(_opciones.RetrasoAcuseMs, ct);
            AcuseSe8? acuse = _registro.Acusar(referencia);
            if (acuse is null)
            {
                return;
            }
            for (int intento = 1; intento <= _opciones.IntentosAviso; intento++)
            {
                try
                {
                    using HttpResponseMessage r = await _http.PostAsJsonAsync(_opciones.UrlAcuse, acuse, ct);
                    if (r.IsSuccessStatusCode)
                    {
                        _registro.AnotarAviso(true);
                        return;
                    }
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    // el adaptador no responde: se reintenta
                }
                await Task.Delay(TimeSpan.FromSeconds(intento), ct);
            }
            _registro.AnotarAviso(false);
            _log.LogWarning("Aviso del acuse de {Referencia} sin respuesta; queda para la consulta por marca.", referencia);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // apagado
        }
    }
}
