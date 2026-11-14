using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.CasosDeUso;

/// <summary>Forma común de los casos de uso que cargan un proyecto, le aplican una operación y lo guardan.</summary>
public abstract class OperacionSobreProyecto<TSolicitud> : Interactor, IPuertoEntrada<TSolicitud, ProyectoDto>
{
    private readonly IRepositorioProyectos _proyectos;
    private readonly GuardadoDeProyectos _guardado;
    protected IReloj Reloj { get; }

    protected OperacionSobreProyecto(IRepositorioProyectos proyectos, GuardadoDeProyectos guardado, IReloj reloj)
    {
        _proyectos = proyectos;
        _guardado = guardado;
        Reloj = reloj;
    }

    protected abstract Guid IdDe(TSolicitud solicitud);
    /// <summary>Llama al método de la entidad que contiene la regla.</summary>
    protected abstract void Aplicar(ProyectoReconstruccion proyecto, TSolicitud solicitud);

    public Task EjecutarAsync(TSolicitud solicitud, IPuertoSalida<ProyectoDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ProyectoReconstruccion? proyecto = await _proyectos.BuscarAsync(IdDe(solicitud), ct);
            if (proyecto is null)
            {
                salida.NoEncontrado("El proyecto no existe.");
                return;
            }
            Aplicar(proyecto, solicitud);
            await _guardado.GuardarAsync(proyecto, ct);
            salida.Exito(proyecto.ADto());
        });
    }
}

// ---------------------------------------------------------------- CU-03 Asignar presupuesto, fuente y responsable

public sealed record SolicitudAsignarPresupuesto(Actor Actor, Guid IdProyecto, decimal? Presupuesto, string? FuenteFinanciacion,
    DateOnly? Inicio, DateOnly? Fin, string Justificacion);
public interface IAsignarPresupuestoEntrada : IPuertoEntrada<SolicitudAsignarPresupuesto, ProyectoDto> { }

public sealed class AsignarPresupuesto : OperacionSobreProyecto<SolicitudAsignarPresupuesto>, IAsignarPresupuestoEntrada
{
    public AsignarPresupuesto(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudAsignarPresupuesto s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudAsignarPresupuesto s)
    {
        if (s.Inicio.HasValue != s.Fin.HasValue)
        {
            throw new ReglaDeNegocioException("CRONOGRAMA_INVALIDO", "El cronograma necesita fecha de inicio y de fin.");
        }
        Dinero? presupuesto = s.Presupuesto.HasValue ? new Dinero(s.Presupuesto.Value) : null;
        Cronograma? cronograma = s.Inicio.HasValue ? new Cronograma(s.Inicio.Value, s.Fin!.Value) : null;
        proyecto.AsignarPresupuesto(s.Actor, presupuesto, s.FuenteFinanciacion, cronograma, s.Justificacion, Reloj.Ahora);
    }
}

public sealed record SolicitudAsignarResponsable(Actor Actor, Guid IdProyecto, string Responsable, string Justificacion);
public interface IAsignarResponsableEntrada : IPuertoEntrada<SolicitudAsignarResponsable, ProyectoDto> { }

public sealed class AsignarResponsable : OperacionSobreProyecto<SolicitudAsignarResponsable>, IAsignarResponsableEntrada
{
    public AsignarResponsable(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudAsignarResponsable s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudAsignarResponsable s)
    {
        proyecto.AsignarResponsable(s.Actor, s.Responsable, s.Justificacion, Reloj.Ahora);
    }
}

// ---------------------------------------------------------------- CU-04 Aprobar

public sealed record SolicitudAprobar(Actor Actor, Guid IdProyecto);
public interface IAprobarProyectoEntrada : IPuertoEntrada<SolicitudAprobar, ProyectoDto> { }

public sealed class AprobarProyecto : OperacionSobreProyecto<SolicitudAprobar>, IAprobarProyectoEntrada
{
    public AprobarProyecto(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudAprobar s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudAprobar s) => proyecto.Aprobar(s.Actor, Reloj.Ahora);
}

// ---------------------------------------------------------------- CU-05 Iniciar ejecución

public sealed record SolicitudIniciar(Actor Actor, Guid IdProyecto, string? Contratista);
public interface IIniciarEjecucionEntrada : IPuertoEntrada<SolicitudIniciar, ProyectoDto> { }

public sealed class IniciarEjecucion : OperacionSobreProyecto<SolicitudIniciar>, IIniciarEjecucionEntrada
{
    public IniciarEjecucion(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudIniciar s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudIniciar s) => proyecto.IniciarEjecucion(s.Actor, s.Contratista, Reloj.Ahora);
}

// ---------------------------------------------------------------- CU-06 Registrar avance y costo

public sealed record SolicitudAvanceYCosto(Actor Actor, Guid IdProyecto, decimal? Avance, decimal? Costo, string? Concepto, string? Justificacion);
public interface IRegistrarAvanceYCostoEntrada : IPuertoEntrada<SolicitudAvanceYCosto, ProyectoDto> { }

public sealed class RegistrarAvanceYCosto : OperacionSobreProyecto<SolicitudAvanceYCosto>, IRegistrarAvanceYCostoEntrada
{
    public RegistrarAvanceYCosto(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudAvanceYCosto s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudAvanceYCosto s)
    {
        if (!s.Avance.HasValue && !s.Costo.HasValue)
        {
            throw new ReglaDeNegocioException("SIN_CAMBIOS", "Indique un avance, un costo o ambos.");
        }
        DateTimeOffset ahora = Reloj.Ahora;
        if (s.Avance.HasValue)
        {
            proyecto.RegistrarAvance(s.Actor, s.Avance.Value, s.Justificacion, ahora);
        }
        if (s.Costo.HasValue)
        {
            proyecto.RegistrarCosto(s.Actor, new Dinero(s.Costo.Value), s.Concepto ?? "", ahora);
        }
    }
}

// ---------------------------------------------------------------- CU-08 Finalizar

public sealed record SolicitudFinalizar(Actor Actor, Guid IdProyecto);
public interface IFinalizarProyectoEntrada : IPuertoEntrada<SolicitudFinalizar, ProyectoDto> { }

public sealed class FinalizarProyecto : OperacionSobreProyecto<SolicitudFinalizar>, IFinalizarProyectoEntrada
{
    public FinalizarProyecto(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudFinalizar s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudFinalizar s) => proyecto.Finalizar(s.Actor, Reloj.Ahora);
}

// ---------------------------------------------------------------- CU-09 Cancelar

public sealed record SolicitudCancelar(Actor Actor, Guid IdProyecto, string Justificacion);
public interface ICancelarProyectoEntrada : IPuertoEntrada<SolicitudCancelar, ProyectoDto> { }

public sealed class CancelarProyecto : OperacionSobreProyecto<SolicitudCancelar>, ICancelarProyectoEntrada
{
    public CancelarProyecto(IRepositorioProyectos p, GuardadoDeProyectos g, IReloj r) : base(p, g, r) { }
    protected override Guid IdDe(SolicitudCancelar s) => s.IdProyecto;
    protected override void Aplicar(ProyectoReconstruccion proyecto, SolicitudCancelar s) => proyecto.Cancelar(s.Actor, s.Justificacion, Reloj.Ahora);
}
