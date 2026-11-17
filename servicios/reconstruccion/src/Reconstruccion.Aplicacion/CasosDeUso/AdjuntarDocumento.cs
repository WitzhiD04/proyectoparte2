using System.Security.Cryptography;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.CasosDeUso;

public sealed record SolicitudDocumento(Actor Actor, Guid IdProyecto, string Nombre, string TipoContenido, Stream Contenido);
public interface IAdjuntarDocumentoEntrada : IPuertoEntrada<SolicitudDocumento, DocumentoDto> { }

/// <summary>CU-07. Guarda el archivo con su huella (hash), autor y fecha.</summary>
public sealed class AdjuntarDocumento : Interactor, IAdjuntarDocumentoEntrada
{
    public const long TamanoMaximo = 20 * 1024 * 1024;

    private readonly IRepositorioProyectos _proyectos;
    private readonly IAlmacenDocumentos _almacen;
    private readonly GuardadoDeProyectos _guardado;
    private readonly IReloj _reloj;

    public AdjuntarDocumento(IRepositorioProyectos proyectos, IAlmacenDocumentos almacen, GuardadoDeProyectos guardado, IReloj reloj)
    {
        _proyectos = proyectos;
        _almacen = almacen;
        _guardado = guardado;
        _reloj = reloj;
    }

    public Task EjecutarAsync(SolicitudDocumento solicitud, IPuertoSalida<DocumentoDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ProyectoReconstruccion? proyecto = await _proyectos.BuscarAsync(solicitud.IdProyecto, ct);
            if (proyecto is null)
            {
                salida.NoEncontrado(MensajeProyectoNoExiste);
                return;
            }

            using MemoryStream memoria = new();
            await solicitud.Contenido.CopyToAsync(memoria, ct);
            if (memoria.Length == 0)
            {
                throw new ReglaDeNegocioException("DOCUMENTO_VACIO", "El archivo está vacío.");
            }
            if (memoria.Length > TamanoMaximo)
            {
                throw new ReglaDeNegocioException("DOCUMENTO_MUY_GRANDE", "El archivo supera los 20 MB.");
            }
            string hash = Convert.ToHexString(SHA256.HashData(memoria.ToArray())).ToLowerInvariant();
            DateTimeOffset ahora = _reloj.Ahora;
            string nombre = Path.GetFileName(solicitud.Nombre);
            string nombreObjeto = $"{proyecto.Id}/{hash[..12]}-{nombre}";

            // Primero se valida con la entidad (ubicación provisional); si pasa, se sube el archivo y se guarda.
            Documento documento = proyecto.AdjuntarDocumento(solicitud.Actor, nombre, solicitud.TipoContenido, nombreObjeto, hash, ahora);
            memoria.Position = 0;
            await _almacen.GuardarAsync(nombreObjeto, memoria, memoria.Length, solicitud.TipoContenido, ct);
            await _guardado.GuardarAsync(proyecto, ct);
            salida.Exito(new DocumentoDto(documento.Id, documento.Nombre, documento.Tipo, documento.Ubicacion, documento.Hash, documento.Autor, documento.Instante));
        });
    }
}
