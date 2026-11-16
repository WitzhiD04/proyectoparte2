using System.Text.Json;
using System.Text.Json.Serialization;
using EvaluacionDanos.Aplicacion.Puertos;

namespace EvaluacionDanos.Infraestructura;

/// <summary>
/// Las mismas opciones de JSON para las respuestas HTTP y para lo que se guarda en jsonb (resultados de operación y
/// contenidos de versión): camelCase, enumeraciones como texto, sin nulos. Son las del prototipo: de ellas depende
/// que un reenvío devuelva exactamente lo mismo (AD-44). El formato del bus es otro y está en comun.
/// </summary>
public static class JsonEvaluacion
{
    public static readonly JsonSerializerOptions Opciones = Configurar(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configurar(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return o;
    }

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Opciones);

    public static T Deserializar<T>(string texto) =>
        JsonSerializer.Deserialize<T>(texto, Opciones) ?? throw new InvalidOperationException("JSON vacío");
}

/// <summary>Adaptador del puerto <see cref="ICodificador"/>.</summary>
public sealed class CodificadorJson : ICodificador
{
    public string Serializar<T>(T valor) => JsonEvaluacion.Serializar(valor);

    public T Deserializar<T>(string texto) => JsonEvaluacion.Deserializar<T>(texto);
}
