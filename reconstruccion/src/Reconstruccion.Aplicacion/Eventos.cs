using System.Text.Json;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion;

/// <summary>Contenido de un evento de proyecto: el cambio y cómo quedó el proyecto después.</summary>
public sealed record EventoProyecto(
    Guid IdProyecto, Guid IdDesastre, string Nombre, int Version, string TipoCambio,
    string? ValorAnterior, string? ValorNuevo, string Autor, string Justificacion, DateTimeOffset Instante,
    string Estado, decimal Presupuesto, decimal CostoEjecutado, decimal Avance);

public static class Eventos
{
    public const string ProyectoCreado = "proyecto.creado";
    public const string AsignacionCambiada = "proyecto.asignacion_cambiada";
    public const string EstadoOAvanceCambiado = "proyecto.estado_o_avance_cambiado";

    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>Un evento por cada entrada de historial nueva del proyecto.</summary>
    public static List<MensajeSalida> DeProyecto(ProyectoReconstruccion proyecto)
    {
        List<MensajeSalida> mensajes = new();
        foreach (EntradaHistorial entrada in proyecto.EntradasNuevas)
        {
            EventoProyecto evento = new(
                proyecto.Id, proyecto.IdDesastre, proyecto.Nombre, entrada.Version, entrada.TipoCambio.ToString(),
                entrada.ValorAnterior, entrada.ValorNuevo, entrada.Autor, entrada.Justificacion, entrada.Instante,
                proyecto.Estado.ToString(), proyecto.Presupuesto?.Monto ?? 0, proyecto.CostoEjecutado.Monto, proyecto.Avance);
            // La clave permite a quien recibe el evento ignorar un repetido.
            string clave = $"{proyecto.Id}:{entrada.Version}";
            mensajes.Add(new MensajeSalida(Guid.NewGuid(), TipoDe(entrada.TipoCambio), clave,
                JsonSerializer.Serialize(evento, Opciones), entrada.Instante));
        }
        return mensajes;
    }

    public static EventoProyecto? Leer(string contenido)
    {
        return JsonSerializer.Deserialize<EventoProyecto>(contenido, Opciones);
    }

    private static string TipoDe(TipoCambio tipo)
    {
        switch (tipo)
        {
            case TipoCambio.Creacion: return ProyectoCreado;
            case TipoCambio.Presupuesto:
            case TipoCambio.Fuente:
            case TipoCambio.Responsable:
            case TipoCambio.Cronograma: return AsignacionCambiada;
            default: return EstadoOAvanceCambiado;
        }
    }
}
