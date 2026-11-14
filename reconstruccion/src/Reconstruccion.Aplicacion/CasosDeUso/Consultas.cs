using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.CasosDeUso;

// ---------------------------------------------------------------- apoyo a las consolas: desastres y proyectos

public sealed record SolicitudDesastres(Actor Actor);
public interface IConsultarDesastresEntrada : IPuertoEntrada<SolicitudDesastres, IReadOnlyList<DesastreDto>> { }

public sealed class ConsultarDesastres : Interactor, IConsultarDesastresEntrada
{
    private readonly ICopiasLocales _copias;
    public ConsultarDesastres(ICopiasLocales copias) { _copias = copias; }

    public Task EjecutarAsync(SolicitudDesastres solicitud, IPuertoSalida<IReadOnlyList<DesastreDto>> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            List<DesastreDto> lista = new();
            foreach (DesastreLocal d in await _copias.DesastresAsync(ct))
            {
                lista.Add(new DesastreDto(d.Id, d.Nombre, d.Estado, d.Estado == EstadosDesastre.EnReconstruccion));
            }
            salida.Exito(lista);
        });
    }
}

public sealed record SolicitudProyectos(Actor Actor, Guid IdDesastre);
public interface IConsultarProyectosEntrada : IPuertoEntrada<SolicitudProyectos, IReadOnlyList<ProyectoDto>> { }

public sealed class ConsultarProyectos : Interactor, IConsultarProyectosEntrada
{
    private readonly IRepositorioProyectos _proyectos;
    public ConsultarProyectos(IRepositorioProyectos proyectos) { _proyectos = proyectos; }

    public Task EjecutarAsync(SolicitudProyectos solicitud, IPuertoSalida<IReadOnlyList<ProyectoDto>> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            List<ProyectoDto> lista = new();
            foreach (ProyectoReconstruccion p in await _proyectos.DeDesastreAsync(solicitud.IdDesastre, ct))
            {
                lista.Add(p.ADto());
            }
            salida.Exito(lista);
        });
    }
}

public sealed record SolicitudProyecto(Actor Actor, Guid IdProyecto);
public interface IConsultarProyectoEntrada : IPuertoEntrada<SolicitudProyecto, ProyectoDto> { }

public sealed class ConsultarProyecto : Interactor, IConsultarProyectoEntrada
{
    private readonly IRepositorioProyectos _proyectos;
    public ConsultarProyecto(IRepositorioProyectos proyectos) { _proyectos = proyectos; }

    public Task EjecutarAsync(SolicitudProyecto solicitud, IPuertoSalida<ProyectoDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ProyectoReconstruccion? proyecto = await _proyectos.BuscarAsync(solicitud.IdProyecto, ct);
            if (proyecto is null)
            {
                salida.NoEncontrado("El proyecto no existe.");
                return;
            }
            salida.Exito(proyecto.ADto());
        });
    }
}

// ---------------------------------------------------------------- CU-10 Consultar historial

public sealed record SolicitudHistorial(Actor Actor, Guid IdProyecto, DateTimeOffset? Fecha);
public interface IConsultarHistorialEntrada : IPuertoEntrada<SolicitudHistorial, HistorialDto> { }

/// <summary>CU-10. Devuelve las entradas en orden y, si se pide una fecha, el proyecto como estaba en esa fecha.</summary>
public sealed class ConsultarHistorial : Interactor, IConsultarHistorialEntrada
{
    private readonly IHistorial _historial;
    public ConsultarHistorial(IHistorial historial) { _historial = historial; }

    public Task EjecutarAsync(SolicitudHistorial solicitud, IPuertoSalida<HistorialDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAlgunRol(solicitud.Actor, Rol.Auditor, Rol.Autoridad);
            IReadOnlyList<EntradaHistorial> entradas = await _historial.DeProyectoAsync(solicitud.IdProyecto, solicitud.Fecha, ct);
            if (entradas.Count == 0 && !solicitud.Fecha.HasValue)
            {
                salida.NoEncontrado("El proyecto no existe.");
                return;
            }
            List<EntradaHistorialDto> dto = new();
            foreach (EntradaHistorial e in entradas.OrderBy(e => e.Version))
            {
                dto.Add(e.ADto());
            }
            EstadoEnFecha? estado = solicitud.Fecha.HasValue ? ReconstruccionHistorica.Reconstruir(entradas) : null;
            salida.Exito(new HistorialDto(solicitud.IdProyecto, solicitud.Fecha, estado, dto));
        });
    }
}

// ---------------------------------------------------------------- CU-11 Consultar consolidado

public sealed record SolicitudConsolidado(Actor Actor, Guid IdDesastre);
public interface IConsultarConsolidadoEntrada : IPuertoEntrada<SolicitudConsolidado, ConsolidadoDto> { }

/// <summary>CU-11. Lee el modelo de lectura del programa, no la base de escritura.</summary>
public sealed class ConsultarConsolidado : Interactor, IConsultarConsolidadoEntrada
{
    private readonly IProyeccionPrograma _proyeccion;
    public ConsultarConsolidado(IProyeccionPrograma proyeccion) { _proyeccion = proyeccion; }

    public Task EjecutarAsync(SolicitudConsolidado solicitud, IPuertoSalida<ConsolidadoDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAlgunRol(solicitud.Actor, Rol.Autoridad, Rol.Auditor);
            IReadOnlyList<FilaPrograma> filas = await _proyeccion.DeDesastreAsync(solicitud.IdDesastre, ct);
            Consolidado c = ConsolidadoPrograma.Calcular(solicitud.IdDesastre, filas);
            Dictionary<string, int> porEstado = new();
            foreach (KeyValuePair<EstadoProyecto, int> par in c.ProyectosPorEstado)
            {
                porEstado[par.Key.ToString()] = par.Value;
            }
            salida.Exito(new ConsolidadoDto(c.IdDesastre, c.TotalProyectos, porEstado, c.PresupuestoAsignado,
                c.CostoEjecutado, c.AvanceAgregado, c.ProyectosEnSobrecosto, c.TodosEnEstadoFinal));
        });
    }
}

// ---------------------------------------------------------------- alimentar el modelo de lectura

/// <summary>Aplica al modelo de lectura un evento de proyecto recibido del bus.</summary>
public sealed class ActualizarProyeccionPrograma
{
    private readonly IProyeccionPrograma _proyeccion;
    public ActualizarProyeccionPrograma(IProyeccionPrograma proyeccion) { _proyeccion = proyeccion; }

    public async Task EjecutarAsync(string contenidoEvento, CancellationToken ct = default)
    {
        EventoProyecto? evento = Eventos.Leer(contenidoEvento);
        if (evento is null)
        {
            return;
        }
        EstadoProyecto estado = Enum.Parse<EstadoProyecto>(evento.Estado);
        await _proyeccion.AplicarAsync(new FilaPrograma(evento.IdProyecto, evento.IdDesastre, evento.Nombre, estado,
            evento.Presupuesto, evento.CostoEjecutado, evento.Avance, evento.Version), ct);
    }
}
