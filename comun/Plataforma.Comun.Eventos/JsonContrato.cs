using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plataforma.Comun.Eventos;

/// <summary>Opciones de serialización del contrato: camelCase, enumeraciones como texto y sin nulos implícitos.</summary>
public static class JsonContrato
{
    public static JsonSerializerOptions Opciones { get; } = Crear();

    private static JsonSerializerOptions Crear()
    {
        JsonSerializerOptions opciones = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
            // Los mensajes del bus no se incrustan en HTML: se escriben tildes y "+" sin escapar, para que el JSON
            // sea legible ("Reconstrucción") y la serialización sea estable al leer y volver a escribir.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        // Sin política de nombres: los valores viajan como están declarados (SIN_DANO, EN_RECONSTRUCCION).
        opciones.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        opciones.MakeReadOnly(populateMissingResolver: true);
        return opciones;
    }
}
