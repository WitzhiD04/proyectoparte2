using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.Puertos;

/// <summary>
/// Puerto de salida: registra los hechos en la misma unidad de trabajo que el cambio, sin guardar (AD-45).
/// El adaptador los traduce al contrato de eventos; la aplicación no conoce el formato del bus.
/// </summary>
public interface IPublicadorHechos
{
    void Registrar(Hecho hecho);
}

public interface IReloj
{
    /// <summary>
    /// UTC truncado a microsegundos (la precisión del almacén), para que el resultado guardado y el devuelto la
    /// primera vez sean idénticos al reenviar.
    /// </summary>
    DateTime Ahora();
}

/// <summary>
/// Texto con el que se guardan el contenido de cada versión y el resultado de cada operación: el mismo formato que
/// las respuestas a la app (camelCase, enumeraciones como texto, sin nulos).
/// </summary>
public interface ICodificador
{
    string Serializar<T>(T valor);
    T Deserializar<T>(string texto);
}

/// <summary>Avisos de operación de la sincronización.</summary>
public interface IBitacoraSincronizacion
{
    void ChoqueDeConcurrencia(Guid? idOperacion, Exception error);
    void OperacionFallida(Guid? idOperacion, Exception error);
}

/// <summary>Un archivo recibido que aún no se ha aceptado, con su SHA-256 (hex en minúsculas) y su tamaño.</summary>
public sealed record ArchivoTemporal(string Ruta, string Sha256, long Bytes);

/// <summary>Puerto de salida hacia el almacén de archivos de evidencias (DA-06). Las claves son inmutables.</summary>
public interface IAlmacenEvidencias
{
    /// <summary>Copia el contenido a un temporal mientras calcula el hash; no deja nada en el almacén.</summary>
    Task<ArchivoTemporal> RecibirAsync(Stream contenido, CancellationToken cancelar);

    Task<bool> ExisteAsync(string clave, CancellationToken cancelar);

    Task GuardarAsync(string clave, ArchivoTemporal archivo, CancellationToken cancelar);

    void Descartar(ArchivoTemporal archivo);
}

/// <summary>Puerto de salida de las copias locales de Gestión de Desastres (E-29) y de la capa de SE-2 (DA-07).</summary>
public interface ICopiasLocales
{
    /// <summary>El desastre de la copia local, en seguimiento; si no existe lo agrega como desconocido.</summary>
    Task<Desastre> DesastreAsync(Guid id, CancellationToken cancelar);

    /// <summary>Guarda (o reemplaza) las divisiones de una parte de la capa y anota la parte como recibida.</summary>
    Task GuardarParteDeCapaAsync(string capa, int versionCapa, int parte, int total,
        IReadOnlyList<DivisionRecibida> divisiones, CancellationToken cancelar);
}

/// <summary>Una división administrativa con su geometría en GeoJSON, tal como llega en EV-21.</summary>
public sealed record DivisionRecibida(string Codigo, string Nombre, string? CodigoPadre, string GeometriaGeoJson);
