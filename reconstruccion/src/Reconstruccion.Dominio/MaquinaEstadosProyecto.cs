namespace Reconstruccion.Dominio;

/// <summary>Tabla de transiciones del diagrama de estados. Lo que no está aquí se rechaza.</summary>
public static class MaquinaEstadosProyecto
{
    private static readonly Dictionary<(EstadoProyecto, AccionProyecto), EstadoProyecto> Transiciones = new()
    {
        { (EstadoProyecto.Formulado, AccionProyecto.Asignar), EstadoProyecto.Formulado },
        { (EstadoProyecto.Aprobado, AccionProyecto.Asignar), EstadoProyecto.Aprobado },
        { (EstadoProyecto.EnEjecucion, AccionProyecto.Asignar), EstadoProyecto.EnEjecucion },
        { (EstadoProyecto.Formulado, AccionProyecto.Aprobar), EstadoProyecto.Aprobado },
        { (EstadoProyecto.Aprobado, AccionProyecto.Iniciar), EstadoProyecto.EnEjecucion },
        { (EstadoProyecto.EnEjecucion, AccionProyecto.RegistrarAvance), EstadoProyecto.EnEjecucion },
        { (EstadoProyecto.EnEjecucion, AccionProyecto.RegistrarCosto), EstadoProyecto.EnEjecucion },
        { (EstadoProyecto.EnEjecucion, AccionProyecto.Finalizar), EstadoProyecto.Finalizado },
        { (EstadoProyecto.Formulado, AccionProyecto.Cancelar), EstadoProyecto.Cancelado },
        { (EstadoProyecto.Aprobado, AccionProyecto.Cancelar), EstadoProyecto.Cancelado },
        { (EstadoProyecto.EnEjecucion, AccionProyecto.Cancelar), EstadoProyecto.Cancelado },
    };

    public static bool EsFinal(EstadoProyecto estado)
    {
        return estado == EstadoProyecto.Finalizado || estado == EstadoProyecto.Cancelado;
    }

    public static bool PuedeEjecutar(EstadoProyecto estado, AccionProyecto accion)
    {
        return Transiciones.ContainsKey((estado, accion));
    }

    /// <summary>Devuelve el estado destino o lanza si la acción no está permitida en ese estado.</summary>
    public static EstadoProyecto Destino(EstadoProyecto estado, AccionProyecto accion)
    {
        if (!Transiciones.TryGetValue((estado, accion), out EstadoProyecto destino))
        {
            throw new ReglaDeNegocioException(
                "TRANSICION_NO_PERMITIDA",
                $"La acción {accion} no está permitida cuando el proyecto está en {estado}.");
        }
        return destino;
    }
}
