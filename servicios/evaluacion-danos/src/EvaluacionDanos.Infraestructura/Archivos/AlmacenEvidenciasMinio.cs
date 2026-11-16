using System.Security.Cryptography;
using EvaluacionDanos.Aplicacion.Puertos;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace EvaluacionDanos.Infraestructura.Archivos;

/// <summary>
/// Evidencias en el almacén de objetos (DA-06, corrige H-13): las réplicas del servicio comparten el mismo bucket,
/// así una evidencia subida por una réplica existe para la otra (AD-24). La base guarda solo la clave y el hash.
/// El contenido se recibe primero en un temporal local (calculando el SHA-256 mientras se copia) y solo se sube al
/// almacén si el hash coincide con el declarado (RF-ED-03).
/// </summary>
public sealed class AlmacenEvidenciasMinio : IAlmacenEvidencias
{
    public const string BucketPorOmision = "evaluacion-danos-evidencias";

    private readonly IConfiguration _configuracion;
    private readonly Lazy<IMinioClient> _cliente;
    private readonly string _temporales = Path.Combine(Path.GetTempPath(), "evidencias-por-verificar");
    private volatile bool _bucketListo;

    public AlmacenEvidenciasMinio(IConfiguration configuracion)
    {
        _configuracion = configuracion;
        // La conexión se arma al primer uso, con la configuración vigente en ese momento.
        _cliente = new Lazy<IMinioClient>(() => new MinioClient()
            .WithEndpoint(Seccion["Endpoint"] ?? "localhost:9000")
            .WithCredentials(Seccion["Usuario"] ?? "minioadmin", Seccion["Clave"] ?? "minioadmin")
            .WithSSL(false)
            .Build());
    }

    private IConfigurationSection Seccion => _configuracion.GetSection("Minio");

    private string Bucket => Seccion["Bucket"] ?? BucketPorOmision;

    /// <summary>Copia el contenido a un temporal y devuelve su ruta, su SHA-256 (hex en minúsculas) y su tamaño.</summary>
    public async Task<ArchivoTemporal> RecibirAsync(Stream contenido, CancellationToken cancelar)
    {
        Directory.CreateDirectory(_temporales);
        var ruta = Path.Combine(_temporales, Guid.NewGuid().ToString("N") + ".part");

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        await using (var destino = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var bufer = new byte[81920];
            int leidos;
            while ((leidos = await contenido.ReadAsync(bufer, cancelar)) > 0)
            {
                sha.AppendData(bufer, 0, leidos);
                await destino.WriteAsync(bufer.AsMemory(0, leidos), cancelar);
                total += leidos;
            }
        }
        return new ArchivoTemporal(ruta, Convert.ToHexStringLower(sha.GetHashAndReset()), total);
    }

    public async Task<bool> ExisteAsync(string clave, CancellationToken cancelar)
    {
        try
        {
            await _cliente.Value.StatObjectAsync(new StatObjectArgs().WithBucket(Bucket).WithObject(clave), cancelar);
            return true;
        }
        catch (Exception ex) when (ex is ObjectNotFoundException or BucketNotFoundException)
        {
            return false;
        }
    }

    public async Task GuardarAsync(string clave, ArchivoTemporal archivo, CancellationToken cancelar)
    {
        await AsegurarBucketAsync(cancelar);
        await using var contenido = new FileStream(archivo.Ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await _cliente.Value.PutObjectAsync(new PutObjectArgs()
            .WithBucket(Bucket)
            .WithObject(clave)
            .WithStreamData(contenido)
            .WithObjectSize(archivo.Bytes)
            .WithContentType("application/octet-stream"), cancelar);
    }

    public void Descartar(ArchivoTemporal archivo)
    {
        if (File.Exists(archivo.Ruta))
        {
            File.Delete(archivo.Ruta);
        }
    }

    private async Task AsegurarBucketAsync(CancellationToken cancelar)
    {
        if (_bucketListo)
        {
            return;
        }
        if (!await _cliente.Value.BucketExistsAsync(new BucketExistsArgs().WithBucket(Bucket), cancelar))
        {
            try
            {
                await _cliente.Value.MakeBucketAsync(new MakeBucketArgs().WithBucket(Bucket), cancelar);
            }
            catch (MinioException) when (_cliente.Value.BucketExistsAsync(new BucketExistsArgs().WithBucket(Bucket), cancelar).Result)
            {
                // Otra réplica lo creó al mismo tiempo.
            }
        }
        _bucketListo = true;
    }
}
