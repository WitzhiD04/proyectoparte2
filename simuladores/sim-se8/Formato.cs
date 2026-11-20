namespace SimSe8;

/// <summary>Alerta en el formato de SE-8. La referencia externa es su clave de idempotencia.</summary>
public sealed record AlertaSe8(
    string? ReferenciaExterna,
    string? Medio,
    string? Destinatario,
    string? Titulo,
    string? Cuerpo,
    string? Prioridad,
    string? CodigoEvento,
    string? Organismo);

/// <summary>Acuse de entrega de SE-8. La secuencia sirve de marca de avance para la consulta de acuses.</summary>
public sealed record AcuseSe8(string ReferenciaExterna, long Secuencia, string Resultado, string? Motivo, DateTimeOffset Instante);

public sealed record RespuestaAlerta(string ReferenciaExterna, bool Nueva, string Estado);

public sealed record PaginaAcuses(IReadOnlyList<AcuseSe8> Acuses, bool HayMas);

public sealed record SolicitudCaida(string Modo);

public static class Vocabulario
{
    public static readonly HashSet<string> Medios = ["EMAIL", "SMS"];
    public static readonly HashSet<string> Prioridades = ["BAJA", "MEDIA", "ALTA", "CRITICA"];
}
