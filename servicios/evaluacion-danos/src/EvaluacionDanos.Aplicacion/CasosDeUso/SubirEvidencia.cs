using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

public enum TipoResultadoSubida
{
    Subida,
    NoDeclarada,
    MuyGrande,
    HashDistinto
}

public sealed record ResultadoSubida(TipoResultadoSubida Tipo, EvidenciaSubidaDto? Evidencia = null, string? Mensaje = null);

/// <summary>
/// PUT /v1/evidencias/{id} (RF-ED-03). Idempotente: una foto ya subida con el mismo hash responde igual sin reescribir.
/// El archivo se acepta solo si su SHA-256 coincide con el que la app declaró en el lote. Es la lógica del prototipo;
/// cambia el destino: un almacén de objetos compartido por las réplicas, con clave inmutable (DA-06, corrige H-13).
/// </summary>
public sealed class SubirEvidencia(IAlmacenEvaluaciones almacen, IAlmacenEvidencias archivos, Parametros parametros)
{
    public async Task<ResultadoSubida> SubirAsync(Guid id, Stream contenido, long tamanoDeclarado, CancellationToken cancelar)
    {
        var evidencia = await almacen.EvidenciaAsync(id, cancelar);
        if (evidencia is null)
        {
            return new ResultadoSubida(TipoResultadoSubida.NoDeclarada);
        }

        long maximo = parametros.TamanoMaximoEvidenciaMb * 1024L * 1024L;
        if (tamanoDeclarado > maximo)
        {
            return MuyGrande(maximo);
        }

        var temporal = await archivos.RecibirAsync(contenido, cancelar);
        try
        {
            if (temporal.Bytes > maximo)
            {
                return MuyGrande(maximo);
            }
            if (!string.Equals(temporal.Sha256, evidencia.Sha256Declarado, StringComparison.OrdinalIgnoreCase))
            {
                return new ResultadoSubida(TipoResultadoSubida.HashDistinto, Mensaje:
                    $"El SHA-256 del archivo ({temporal.Sha256}) no coincide con el declarado ({evidencia.Sha256Declarado})");
            }

            string clave = Evidencia.ClaveDe(evidencia.EvaluacionId, evidencia.Id);
            bool existe = await archivos.ExisteAsync(clave, cancelar);
            if (evidencia.Subida && evidencia.ClaveObjeto == clave && existe)
            {
                return Subida(evidencia);   // reenvío: mismo hash, no se reescribe
            }

            // La clave es inmutable: si el objeto ya está (se guardó y el proceso cayó antes de anotarlo), no se
            // vuelve a escribir. Su contenido es el mismo, porque el hash declarado es parte de la evidencia.
            if (!existe)
            {
                await archivos.GuardarAsync(clave, temporal, cancelar);
            }
            evidencia.Subida = true;
            evidencia.ClaveObjeto = clave;
            evidencia.InstanteSubida = DateTime.UtcNow;
            evidencia.TamanoBytes = temporal.Bytes;
            await almacen.GuardarAsync(cancelar);
            return Subida(evidencia);
        }
        finally
        {
            archivos.Descartar(temporal);
        }
    }

    private static ResultadoSubida Subida(Evidencia e) =>
        new(TipoResultadoSubida.Subida, new EvidenciaSubidaDto(e.Id, e.EvaluacionId, e.Subida, e.InstanteSubida, e.TamanoBytes));

    private static ResultadoSubida MuyGrande(long maximo) =>
        new(TipoResultadoSubida.MuyGrande, Mensaje: $"El archivo supera el máximo de {maximo / (1024 * 1024)} MB");
}
