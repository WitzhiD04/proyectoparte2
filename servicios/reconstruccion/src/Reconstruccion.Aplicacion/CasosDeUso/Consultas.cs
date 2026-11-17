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
                // Una autoridad solo ve los proyectos de su jurisdicción (Cuadro 3 del SRS, DA-14).
                if (Jurisdiccion.Cubre(solicitud.Actor, p.CodigosMunicipio))
                {
                    lista.Add(p.ADto());
                }
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
            // Fuera de la jurisdicción de la autoridad responde igual que si no existiera: no revela que existe.
            if (proyecto is null || !Jurisdiccion.Cubre(solicitud.Actor, proyecto.CodigosMunicipio))
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

/// <summary>
/// CU-10. Devuelve las entradas en orden y, si se pide una fecha, el proyecto como estaba en esa fecha. A una autoridad
/// solo le muestra los proyectos de su jurisdicción; fuera de ella responde como si no existiera (DA-14).
/// </summary>
public sealed class ConsultarHistorial : Interactor, IConsultarHistorialEntrada
{
    private readonly IHistorial _historial;
    private readonly IRepositorioProyectos _proyectos;

    public ConsultarHistorial(IHistorial historial, IRepositorioProyectos proyectos)
    {
        _historial = historial;
        _proyectos = proyectos;
    }

    public Task EjecutarAsync(SolicitudHistorial solicitud, IPuertoSalida<HistorialDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAlgunRol(solicitud.Actor, Rol.Auditor, Rol.Autoridad);
            if (solicitud.Actor.Rol == Rol.Autoridad)
            {
                ProyectoReconstruccion? proyecto = await _proyectos.BuscarAsync(solicitud.IdProyecto, ct);
                if (proyecto is null || !Jurisdiccion.Cubre(solicitud.Actor, proyecto.CodigosMunicipio))
                {
                    salida.NoEncontrado("El proyecto no existe.");
                    return;
                }
            }
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

/// <summary>
/// CU-11. Lee el modelo de lectura del programa, no la base de escritura. Una autoridad solo cuenta los proyectos de su
/// jurisdicción (DA-14 y Cuadro 3 del SRS); el auditor ve todo el desastre.
/// </summary>
public sealed class ConsultarConsolidado : Interactor, IConsultarConsolidadoEntrada
{
    private readonly IProyeccionPrograma _proyeccion;
    public ConsultarConsolidado(IProyeccionPrograma proyeccion) { _proyeccion = proyeccion; }

    public Task EjecutarAsync(SolicitudConsolidado solicitud, IPuertoSalida<ConsolidadoDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAlgunRol(solicitud.Actor, Rol.Autoridad, Rol.Auditor);
            IReadOnlyList<FilaPrograma> filas = (await _proyeccion.DeDesastreAsync(solicitud.IdDesastre, ct))
                .Where(f => Jurisdiccion.Cubre(solicitud.Actor, f.CodigosMunicipio))
                .ToList();
            Consolidado c = ConsolidadoPrograma.Calcular(solicitud.IdDesastre, filas);
            Dictionary<string, int> porEstado = new();
            foreach (KeyValuePair<EstadoProyecto, int> par in c.ProyectosPorEstado)
            {
                porEstado[par.Key.ToString()] = par.Value;
            }
            DateTimeOffset? datoAl = filas.Count == 0 ? null : filas.Max(f => f.ActualizadoEn);
            salida.Exito(new ConsolidadoDto(c.IdDesastre, c.TotalProyectos, porEstado, c.PresupuestoAsignado,
                c.CostoEjecutado, c.AvanceAgregado, c.ProyectosEnSobrecosto, c.TodosEnEstadoFinal, datoAl));
        });
    }
}

// ---------------------------------------------------------------- alimentar el modelo de lectura

/// <summary>
/// Aplica al modelo de lectura el estado de un proyecto que llegó de Reconstrucción. Recibe la fila ya traducida
/// por el adaptador de entrada: el caso de uso no conoce el formato del bus. Repetir o recibir tarde un cambio no
/// altera nada, porque la proyección solo acepta una versión más nueva.
/// </summary>
public sealed class ActualizarProyeccionPrograma
{
    private readonly IProyeccionPrograma _proyeccion;
    public ActualizarProyeccionPrograma(IProyeccionPrograma proyeccion) { _proyeccion = proyeccion; }

    public Task EjecutarAsync(FilaPrograma fila, CancellationToken ct = default)
    {
        return _proyeccion.AplicarAsync(fila, ct);
    }
}
