using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.CasosDeUso;

/// <summary>Otro usuario cambió lo mismo al tiempo. Lo lanza la unidad de trabajo al confirmar.</summary>
public class ConflictoDeConcurrenciaException : Exception
{
    public ConflictoDeConcurrenciaException(string mensaje) : base(mensaje) { }
}

/// <summary>Base de los interactors: convierte las excepciones del dominio en llamadas al puerto de salida.</summary>
public abstract class Interactor
{
    protected static async Task ProtegerAsync<T>(IPuertoSalida<T> salida, Func<Task> accion)
    {
        try
        {
            await accion();
        }
        catch (NoAutorizadoException e)
        {
            salida.NoAutorizado(e.Message);
        }
        catch (ReglaDeNegocioException e)
        {
            salida.ReglaIncumplida(e.Codigo, e.Message);
        }
        catch (ConflictoDeConcurrenciaException e)
        {
            salida.ReglaIncumplida("CONFLICTO", e.Message);
        }
    }

    protected static void ExigirAlgunRol(Actor actor, params Rol[] roles)
    {
        if (!roles.Contains(actor.Rol))
        {
            throw new NoAutorizadoException("El rol " + actor.Rol + " no puede hacer esta operación.");
        }
    }
}

/// <summary>
/// Guarda en una sola transacción el proyecto, sus entradas de historial y sus eventos.
/// Todos los casos de uso que cambian un proyecto pasan por aquí.
/// </summary>
public sealed class GuardadoDeProyectos
{
    private readonly IRepositorioProyectos _proyectos;
    private readonly IHistorial _historial;
    private readonly IPublicadorHechos _publicador;
    private readonly IUnidadDeTrabajo _unidad;

    public GuardadoDeProyectos(IRepositorioProyectos proyectos, IHistorial historial, IPublicadorHechos publicador, IUnidadDeTrabajo unidad)
    {
        _proyectos = proyectos;
        _historial = historial;
        _publicador = publicador;
        _unidad = unidad;
    }

    public Task GuardarAsync(ProyectoReconstruccion proyecto, CancellationToken ct)
    {
        return GuardarAsync(new[] { proyecto }, null, ct);
    }

    public async Task GuardarAsync(IReadOnlyList<ProyectoReconstruccion> proyectos, LoteCreacion? lote, CancellationToken ct)
    {
        foreach (ProyectoReconstruccion proyecto in proyectos)
        {
            await _proyectos.GuardarAsync(proyecto, ct);
            await _historial.AgregarAsync(proyecto.EntradasNuevas, ct);
            await _publicador.RegistrarAsync(HechosDeProyecto.De(proyecto), ct);
        }
        if (lote is not null)
        {
            await _proyectos.GuardarLoteAsync(lote, ct);
        }
        await _unidad.ConfirmarAsync(ct);
        foreach (ProyectoReconstruccion proyecto in proyectos)
        {
            proyecto.LimpiarPendientes();
        }
    }
}
