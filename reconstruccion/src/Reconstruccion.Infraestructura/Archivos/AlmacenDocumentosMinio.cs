using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Infraestructura.Archivos;

/// <summary>Guarda los documentos y evidencias en MinIO.</summary>
public sealed class AlmacenDocumentosMinio : IAlmacenDocumentos
{
    private readonly IMinioClient _cliente;
    private readonly string _bucket;
    private bool _bucketListo;

    public AlmacenDocumentosMinio(IConfiguration configuracion)
    {
        IConfigurationSection seccion = configuracion.GetSection("Minio");
        _bucket = seccion["Bucket"] ?? "reconstruccion-documentos";
        _cliente = new MinioClient()
            .WithEndpoint(seccion["Endpoint"] ?? "localhost:9000")
            .WithCredentials(seccion["Usuario"] ?? "minioadmin", seccion["Clave"] ?? "minioadmin")
            .WithSSL(false)
            .Build();
    }

    public async Task<string> GuardarAsync(string nombreObjeto, Stream contenido, long tamano, string tipoContenido, CancellationToken ct = default)
    {
        if (!_bucketListo)
        {
            bool existe = await _cliente.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
            if (!existe)
            {
                await _cliente.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
            }
            _bucketListo = true;
        }
        await _cliente.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(nombreObjeto)
            .WithStreamData(contenido)
            .WithObjectSize(tamano)
            .WithContentType(tipoContenido), ct);
        return $"{_bucket}/{nombreObjeto}";
    }
}
