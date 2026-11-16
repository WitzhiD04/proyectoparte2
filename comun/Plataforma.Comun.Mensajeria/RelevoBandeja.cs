using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Plataforma.Comun.Mensajeria;

/// <summary>
/// Relevo de la bandeja de salida hacia el bus (AD-45, DA-04). Trabaja en dos fases:
/// <list type="number">
/// <item>Numerar: bajo el bloqueo del contador del productor asigna una secuencia consecutiva a los eventos ya
/// confirmados. Así la secuencia no tiene huecos (mitigación de ATAM R-02), aunque haya varias réplicas.</item>
/// <item>Publicar: envía los eventos numerados con confirmación del bus y <c>mandatory</c>, y los marca como
/// publicados solo cuando el bus los acepta. Si el bus los rechaza (cola llena, sin ruta, nodo caído), el evento
/// queda en la bandeja y se reintenta: nunca se marca un evento que el bus no aceptó (QA-04).</item>
/// </list>
/// Si el proceso cae después de publicar y antes de marcar, el evento se republica con el mismo número y la misma
/// clave de idempotencia; los consumidores lo descartan. Los eventos publicados se conservan (reenvío tras una
/// conmutación, R-09).
/// </summary>
public sealed class RelevoBandeja<TContexto> : BackgroundService where TContexto : DbContext
{
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _ambitos;
    private readonly ConexionBus _bus;
    private readonly OpcionesBus _opciones;
    private readonly ILogger<RelevoBandeja<TContexto>> _log;
    private IChannel? _canal;

    public RelevoBandeja(IServiceScopeFactory ambitos, ConexionBus bus, OpcionesBus opciones, ILogger<RelevoBandeja<TContexto>> log)
    {
        if (string.IsNullOrWhiteSpace(opciones.Productor))
        {
            throw new InvalidOperationException("Falta Bus:Productor: el relevo necesita el nombre del productor.");
        }
        _ambitos = ambitos;
        _bus = bus;
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        TimeSpan intervalo = TimeSpan.FromMilliseconds(_opciones.IntervaloRelevoMs);
        TimeSpan espera = intervalo;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int numerados = await NumerarAsync(ct);
                ResultadoPublicacion resultado = await PublicarAsync(ct);
                if (resultado.Rechazado)
                {
                    espera = TimeSpan.FromTicks(Math.Min(EsperaMaxima.Ticks, Math.Max(intervalo.Ticks, espera.Ticks * 2)));
                }
                else
                {
                    espera = intervalo;
                    if (numerados == _opciones.TamanoLote || resultado.Publicados == _opciones.TamanoLote)
                    {
                        continue; // puede haber más: no esperar
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "El relevo no pudo avanzar; los eventos siguen en la bandeja y se reintenta.");
                await CerrarCanalAsync();
                espera = TimeSpan.FromTicks(Math.Min(EsperaMaxima.Ticks, Math.Max(intervalo.Ticks, espera.Ticks * 2)));
            }
            await Task.Delay(espera, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
        await CerrarCanalAsync();
    }

    /// <summary>Fase 1. Devuelve cuántos eventos numeró.</summary>
    public async Task<int> NumerarAsync(CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        TContexto db = ambito.ServiceProvider.GetRequiredService<TContexto>();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        string productor = _opciones.Productor;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO contador_productor (productor, ultimo) VALUES ({productor}, 0) ON CONFLICT (productor) DO NOTHING", ct);
        // El bloqueo del contador serializa la numeración entre réplicas: mientras una numera, la otra espera.
        // ToListAsync y no SingleAsync: así EF no envuelve el SQL en una subconsulta y el FOR UPDATE queda donde está.
        long ultimo = (await db.Database
            .SqlQuery<long>($"SELECT ultimo AS \"Value\" FROM contador_productor WHERE productor = {productor} FOR UPDATE")
            .ToListAsync(ct)).Single();

        List<Guid> pendientes = await db.Database
            .SqlQuery<Guid>($@"SELECT id AS ""Value"" FROM bandeja_salida WHERE secuencia_productor IS NULL
                               ORDER BY instante, orden, secuencia LIMIT {_opciones.TamanoLote} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (pendientes.Count == 0)
        {
            await transaccion.CommitAsync(ct);
            return 0;
        }

        // La consulta devuelve las filas en el orden de los hechos; los números se asignan en ese mismo orden.
        long[] secuencias = Enumerable.Range(1, pendientes.Count).Select(i => ultimo + i).ToArray();
        Guid[] ids = pendientes.ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE bandeja_salida AS b SET secuencia_productor = v.secuencia
            FROM unnest({ids}, {secuencias}) AS v(id, secuencia) WHERE b.id = v.id", ct);
        long nuevoUltimo = secuencias[^1];
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE contador_productor SET ultimo = {nuevoUltimo} WHERE productor = {productor}", ct);
        await transaccion.CommitAsync(ct);
        return pendientes.Count;
    }

    /// <summary>Fase 2. Publica en orden de secuencia y se detiene en el primer rechazo, para conservar el orden.</summary>
    public async Task<ResultadoPublicacion> PublicarAsync(CancellationToken ct)
    {
        using IServiceScope ambito = _ambitos.CreateScope();
        TContexto db = ambito.ServiceProvider.GetRequiredService<TContexto>();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        List<FilaBandeja> filas = await db.Set<FilaBandeja>()
            .FromSqlInterpolated($@"SELECT * FROM bandeja_salida
                                    WHERE secuencia_productor IS NOT NULL AND publicado_en IS NULL
                                    ORDER BY secuencia_productor LIMIT {_opciones.TamanoLote} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (filas.Count == 0)
        {
            await transaccion.CommitAsync(ct);
            return new ResultadoPublicacion(0, false);
        }

        int publicados = 0;
        bool rechazado = false;
        IChannel canal = await CanalAsync(ct);
        foreach (FilaBandeja fila in filas)
        {
            try
            {
                await PublicarUnoAsync(canal, fila, ct);
            }
            catch (Exception e) when (e is PublishException or AlreadyClosedException or OperationInterruptedException)
            {
                _log.LogWarning(e, "El bus no aceptó el evento {Tipo} {Id} (secuencia {Secuencia}); queda en la bandeja.",
                    fila.Tipo, fila.Id, fila.SecuenciaProductor);
                rechazado = true;
                await CerrarCanalAsync();
                break;
            }
            fila.PublicadoEn = DateTimeOffset.UtcNow;
            publicados++;
        }
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return new ResultadoPublicacion(publicados, rechazado);
    }

    private async Task PublicarUnoAsync(IChannel canal, FilaBandeja fila, CancellationToken ct)
    {
        Sobre sobre = Sobre.Leer(fila.Sobre) with { SecuenciaProductor = fila.SecuenciaProductor };
        BasicProperties propiedades = new()
        {
            MessageId = sobre.IdEvento.ToString(),
            Type = sobre.Tipo,
            AppId = sobre.Productor,
            CorrelationId = sobre.IdCorrelacion,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(sobre.InstanteEmision.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["productor"] = sobre.Productor,
                ["secuencia-productor"] = sobre.SecuenciaProductor,
                ["clave-idempotencia"] = sobre.ClaveIdempotencia,
            },
        };
        // Con confirmaciones y mandatory, la llamada termina cuando el bus acepta el mensaje y lanza
        // PublishException si lo rechaza (nack por cola llena) o lo devuelve (sin ruta).
        await canal.BasicPublishAsync(CatalogoEventos.Exchange, fila.ClaveRuteo, mandatory: true, propiedades,
            Encoding.UTF8.GetBytes(sobre.Serializar()), ct);
    }

    private async Task<IChannel> CanalAsync(CancellationToken ct)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }
        await CerrarCanalAsync();
        IConnection conexion = await _bus.ObtenerAsync(ct);
        _canal = await conexion.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        return _canal;
    }

    private async Task CerrarCanalAsync()
    {
        try
        {
            if (_canal is not null)
            {
                await _canal.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // el canal ya estaba cerrado
        }
        _canal = null;
    }
}

public sealed record ResultadoPublicacion(int Publicados, bool Rechazado);
