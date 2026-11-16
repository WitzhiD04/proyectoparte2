using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Plataforma.Comun.Mensajeria;

/// <summary>
/// Conexión compartida con el clúster de RabbitMQ. Recibe la lista de nodos y el cliente se conecta al que responda;
/// si la conexión cae, la recuperación automática del cliente la restablece.
/// Los servicios no declaran topología: el exchange, las colas y las políticas se declaran al desplegar (DA-03).
/// </summary>
public sealed class ConexionBus : IAsyncDisposable, IDisposable
{
    private readonly OpcionesBus _opciones;
    private readonly ILogger<ConexionBus> _log;
    private readonly SemaphoreSlim _candado = new(1, 1);
    private IConnection? _conexion;

    public ConexionBus(OpcionesBus opciones, ILogger<ConexionBus> log)
    {
        _opciones = opciones;
        _log = log;
    }

    public async Task<IConnection> ObtenerAsync(CancellationToken ct)
    {
        if (_conexion is { IsOpen: true })
        {
            return _conexion;
        }
        await _candado.WaitAsync(ct);
        try
        {
            if (_conexion is { IsOpen: true })
            {
                return _conexion;
            }
            if (_conexion is not null)
            {
                await _conexion.DisposeAsync();
            }
            ConnectionFactory fabrica = new()
            {
                UserName = _opciones.Usuario,
                Password = _opciones.Clave,
                VirtualHost = _opciones.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = false,
                ClientProvidedName = _opciones.Productor,
            };
            List<AmqpTcpEndpoint> nodos = (_opciones.Nodos.Count > 0 ? _opciones.Nodos : ["localhost"])
                .Select(Nodo).ToList();
            _conexion = await fabrica.CreateConnectionAsync(nodos, ct);
            _log.LogInformation("Conectado al bus en {Nodo}.", _conexion.Endpoint);
            return _conexion;
        }
        finally
        {
            _candado.Release();
        }
    }

    /// <summary>Comprueba sin modificar nada que el exchange y la cola existan (declaración pasiva). Nulo: no se verifica.</summary>
    public async Task VerificarTopologiaAsync(string? exchange, string? cola, CancellationToken ct)
    {
        IConnection conexion = await ObtenerAsync(ct);
        await using IChannel canal = await conexion.CreateChannelAsync(cancellationToken: ct);
        if (!string.IsNullOrWhiteSpace(exchange))
        {
            await canal.ExchangeDeclarePassiveAsync(exchange, ct);
        }
        if (!string.IsNullOrWhiteSpace(cola))
        {
            await canal.QueueDeclarePassiveAsync(cola, ct);
        }
    }

    private static AmqpTcpEndpoint Nodo(string texto)
    {
        string[] partes = texto.Split(':', 2);
        return partes.Length == 2 ? new AmqpTcpEndpoint(partes[0], int.Parse(partes[1])) : new AmqpTcpEndpoint(partes[0]);
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexion is not null)
        {
            await _conexion.DisposeAsync();
            _conexion = null;
        }
        _candado.Dispose();
    }

    public void Dispose()
    {
        _conexion?.Dispose();
        _conexion = null;
        _candado.Dispose();
    }
}
