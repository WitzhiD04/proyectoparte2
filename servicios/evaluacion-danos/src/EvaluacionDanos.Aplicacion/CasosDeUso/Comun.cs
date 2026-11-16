using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

/// <summary>
/// Piezas comunes de los cambios que se hacen en el servidor sin pasar por la sincronización: validación,
/// actualización con conexión, asociación y resolución. Todos conservan la versión anterior (AD-30).
/// </summary>
internal static class CambioEnServidor
{
    /// <summary>
    /// Agrega al historial una versión con el contenido actual de la evaluación (o el que se indique) y devuelve su
    /// número. Nunca se modifica una versión existente.
    /// </summary>
    public static async Task<int> NuevaVersionAsync(IAlmacenEvaluaciones almacen, ICodificador codificador,
        Evaluacion evaluacion, MotivoVersion motivo, string autor, string? detalle, DateTime ahora,
        CancellationToken cancelar, string? contenidoJson = null)
    {
        int numero = await almacen.UltimoNumeroVersionAsync(evaluacion.Id, cancelar) + 1;
        almacen.Agregar(new EvaluacionVersion
        {
            Id = Guid.NewGuid(),
            EvaluacionId = evaluacion.Id,
            NumeroVersion = numero,
            ContenidoJson = contenidoJson ?? codificador.Serializar(evaluacion.ComoContenido()),
            Autor = Recortar(autor, Limites.Nombre),
            IdDispositivo = EvaluacionVersion.DispositivoServidor,
            EnConflicto = false,
            Instante = ahora,
            Motivo = motivo,
            Detalle = detalle
        });
        return numero;
    }

    /// <summary>
    /// Sube la versión del activo y anota quién la produjo. Para la regla de conflictos (AD-43) el autor cuenta como
    /// otro dispositivo: una operación hecha sin conexión sobre una base anterior a este cambio queda en conflicto y
    /// va a revisión, en lugar de sobrescribirlo en silencio (AD-32, QA-14). La regla no cambia; solo ve una versión más.
    /// </summary>
    /// <param name="evaluacionVigente">La evaluación que queda vigente con esta versión, o null si no cambia.</param>
    public static void SubirVersionDelActivo(IAlmacenEvaluaciones almacen, Activo activo, string autor,
        Guid? evaluacionVigente, DateTime ahora)
    {
        activo.Version++;   // token de concurrencia: si otro cambio llegó primero, el guardado falla y se reintenta
        activo.ActualizadoEn = ahora;
        almacen.Agregar(new ActivoVersion
        {
            ActivoId = activo.Id,
            Numero = activo.Version,
            IdDispositivo = autor,
            EvaluacionId = evaluacionVigente,
            Instante = ahora
        });
    }

    /// <summary>Guarda y traduce el choque con otro cambio a un fallo que la capa de entrada presenta como 409.</summary>
    public static async Task<Resultado<T>?> GuardarAsync<T>(IAlmacenEvaluaciones almacen, CancellationToken cancelar)
    {
        try
        {
            await almacen.GuardarAsync(cancelar);
            return null;
        }
        catch (ConcurrenciaException)
        {
            return Resultado<T>.Falla(TipoFallo.Concurrencia,
                "Otro cambio modificó el mismo dato al mismo tiempo; vuelva a consultar e inténtelo de nuevo");
        }
    }

    public static EvaluacionDetalleDto Detalle(Evaluacion e, Activo activo, int numeroVersion) => new(
        e.Id, e.ActivoId, e.DesastreId, e.IdDispositivo, e.Estado, e.NivelDano, e.TipoActivoCodigo, e.Descripcion,
        e.Lat, e.Lon, e.PrecisionM, e.Evaluador, e.Organizacion, e.InstanteCampo, e.InstanteServidor, numeroVersion,
        activo.Version, activo.CodigoMunicipio, e.ValidadoPor, e.InstanteValidacion, e.DuplicadoDe);

    /// <summary>EV-08 exige una referencia del activo; la app permite dejarla vacía.</summary>
    public static string Referencia(Activo activo) =>
        string.IsNullOrWhiteSpace(activo.Referencia) ? $"{activo.TipoActivoCodigo} sin referencia" : activo.Referencia;

    public static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
