using GestionDesastres.Dominio;

namespace GestionDesastres.Aplicacion.CasosDeUso;

/// <summary>Base de los interactores: convierte las excepciones del dominio en llamadas al puerto de salida.</summary>
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
}

/// <summary>Guarda en una sola transacción el desastre, su historial, su zona y sus eventos (AD-20, AD-45).</summary>
public sealed class GuardadoDeDesastres
{
    private readonly IRepositorioDesastres _desastres;
    private readonly IPublicadorHechos _publicador;
    private readonly IUnidadDeTrabajo _unidad;

    public GuardadoDeDesastres(IRepositorioDesastres desastres, IPublicadorHechos publicador, IUnidadDeTrabajo unidad)
    {
        _desastres = desastres;
        _publicador = publicador;
        _unidad = unidad;
    }

    public async Task GuardarAsync(Desastre desastre, CancellationToken ct)
    {
        await _desastres.GuardarAsync(desastre, ct);
        await _publicador.RegistrarAsync(HechosDeDesastre.De(desastre), ct);
        await _unidad.ConfirmarAsync(ct);
        desastre.LimpiarPendientes();
    }
}

/// <summary>Carga el perfil vigente y el perfil mínimo de un nivel; ambos son datos del catálogo (QA-10).</summary>
internal static class Perfiles
{
    public static async Task<PerfilCicloVida> ExigirAsync(ICatalogo catalogo, string codigo, CancellationToken ct) =>
        await catalogo.PerfilAsync(codigo, ct)
        ?? throw new ReglaDeNegocioException("PERFIL_DESCONOCIDO", $"El perfil {codigo} no está en el catálogo.");

    public static async Task<PerfilCicloVida> MinimoAsync(ICatalogo catalogo, Severidad nivel, CancellationToken ct) =>
        await ExigirAsync(catalogo, (await catalogo.PerfilPorNivelAsync(ct)).MinimoPara(nivel), ct);
}

// -------------------------------------------------------------------- registro (RF-GD-02 a 05, RF-GD-08, RF-GD-12)

public sealed record SolicitudRegistro(
    Actor Actor, string Tipo, string Nombre, string Descripcion, DateTimeOffset InstanteInicio, double Lat, double Lon,
    Severidad Severidad, string Responsable, IReadOnlyList<Participante> Participantes,
    string? GeometriaZona, IReadOnlyList<string> MunicipiosZona, FuenteDelimitacion FuenteZona);

public interface IRegistrarDesastreEntrada : IPuertoEntrada<SolicitudRegistro, DesastreDto> { }

public sealed class RegistrarDesastre : Interactor, IRegistrarDesastreEntrada
{
    private readonly ICatalogo _catalogo;
    private readonly ICapaMunicipios _capa;
    private readonly GuardadoDeDesastres _guardado;
    private readonly IReloj _reloj;

    public RegistrarDesastre(ICatalogo catalogo, ICapaMunicipios capa, GuardadoDeDesastres guardado, IReloj reloj)
    {
        _catalogo = catalogo;
        _capa = capa;
        _guardado = guardado;
        _reloj = reloj;
    }

    public Task EjecutarAsync(SolicitudRegistro s, IPuertoSalida<DesastreDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            TipoDesastre tipo = await _catalogo.TipoAsync(s.Tipo, ct)
                ?? throw new ReglaDeNegocioException("TIPO_DESCONOCIDO", $"El tipo {s.Tipo} no está en el catálogo (RF-GD-04).");
            PerfilCicloVida perfilTipo = await Perfiles.ExigirAsync(_catalogo, tipo.CodigoPerfil, ct);
            PerfilCicloVida perfilMinimo = await Perfiles.MinimoAsync(_catalogo, s.Severidad, ct);
            (string? geometria, IReadOnlyList<string> municipios) = await _capa.CompletarAsync(s.GeometriaZona, s.MunicipiosZona, ct);

            Desastre desastre = Desastre.Registrar(Guid.NewGuid(), tipo, perfilTipo, perfilMinimo, s.Nombre, s.Descripcion, s.InstanteInicio,
                new Ubicacion(s.Lat, s.Lon), s.Severidad, s.Responsable, s.Participantes, geometria, municipios, s.FuenteZona, s.Actor, _reloj.Ahora);
            await _guardado.GuardarAsync(desastre, ct);
            salida.Exito(Mapeos.ADto(desastre, await _catalogo.PerfilAsync(desastre.CodigoPerfil, ct)));
        });
    }
}

// -------------------------------------------------------------------- operaciones sobre un desastre existente

public sealed record SolicitudReclasificar(Actor Actor, Guid IdDesastre, Severidad Severidad, string Justificacion);
public sealed record SolicitudTransicion(Actor Actor, Guid IdDesastre, EstadoDesastre Destino, string? Justificacion);
public sealed record SolicitudDelimitar(Actor Actor, Guid IdDesastre, string? Geometria, IReadOnlyList<string> Municipios,
    FuenteDelimitacion Fuente, string Justificacion);
public sealed record SolicitudOrganismos(Actor Actor, Guid IdDesastre, string Responsable, IReadOnlyList<Participante> Participantes,
    string Justificacion);
public sealed record SolicitudDesastre(Actor Actor, Guid IdDesastre);

public interface IReclasificarEntrada : IPuertoEntrada<SolicitudReclasificar, DesastreDto> { }
public interface ICambiarEstadoEntrada : IPuertoEntrada<SolicitudTransicion, DesastreDto> { }
public interface IDelimitarEntrada : IPuertoEntrada<SolicitudDelimitar, DesastreDto> { }
public interface IAsignarOrganismosEntrada : IPuertoEntrada<SolicitudOrganismos, DesastreDto> { }
public interface IConsultarDesastreEntrada : IPuertoEntrada<SolicitudDesastre, DesastreDto> { }
public interface IConsultarHistorialEntrada : IPuertoEntrada<SolicitudDesastre, HistorialDto> { }

/// <summary>Base de las operaciones que cargan un desastre, lo cambian y lo guardan.</summary>
public abstract class OperacionSobreDesastre<TSolicitud> : Interactor, IPuertoEntrada<TSolicitud, DesastreDto>
{
    protected readonly IRepositorioDesastres Desastres;
    protected readonly ICatalogo Catalogo;
    protected readonly GuardadoDeDesastres Guardado;
    protected readonly IReloj Reloj;

    protected OperacionSobreDesastre(IRepositorioDesastres desastres, ICatalogo catalogo, GuardadoDeDesastres guardado, IReloj reloj)
    {
        Desastres = desastres;
        Catalogo = catalogo;
        Guardado = guardado;
        Reloj = reloj;
    }

    protected abstract Guid IdDe(TSolicitud solicitud);
    protected abstract Task AplicarAsync(Desastre desastre, TSolicitud solicitud, CancellationToken ct);

    public Task EjecutarAsync(TSolicitud solicitud, IPuertoSalida<DesastreDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            Desastre? desastre = await Desastres.BuscarAsync(IdDe(solicitud), ct);
            if (desastre is null)
            {
                salida.NoEncontrado($"No existe el desastre {IdDe(solicitud)}.");
                return;
            }
            await AplicarAsync(desastre, solicitud, ct);
            await Guardado.GuardarAsync(desastre, ct);
            salida.Exito(Mapeos.ADto(desastre, await Catalogo.PerfilAsync(desastre.CodigoPerfil, ct)));
        });
    }
}

/// <summary>RF-GD-06: reclasificación con cambio de perfil si el nivel lo exige (RN-2, RN-3). Publica EV-04.</summary>
public sealed class Reclasificar : OperacionSobreDesastre<SolicitudReclasificar>, IReclasificarEntrada
{
    public Reclasificar(IRepositorioDesastres d, ICatalogo c, GuardadoDeDesastres g, IReloj r) : base(d, c, g, r) { }
    protected override Guid IdDe(SolicitudReclasificar s) => s.IdDesastre;

    protected override async Task AplicarAsync(Desastre desastre, SolicitudReclasificar s, CancellationToken ct)
    {
        PerfilCicloVida actual = await Perfiles.ExigirAsync(Catalogo, desastre.CodigoPerfil, ct);
        PerfilCicloVida minimo = await Perfiles.MinimoAsync(Catalogo, s.Severidad, ct);
        desastre.Reclasificar(s.Severidad, s.Justificacion, actual, minimo, s.Actor, Reloj.Ahora);
    }
}

/// <summary>RF-GD-07 y RF-GD-18: transición validada contra el perfil; el cierre exige proyectos en estado final. Publica EV-06.</summary>
public sealed class CambiarEstado : OperacionSobreDesastre<SolicitudTransicion>, ICambiarEstadoEntrada
{
    private readonly IProyectosDelDesastre _proyectos;

    public CambiarEstado(IRepositorioDesastres d, ICatalogo c, GuardadoDeDesastres g, IReloj r, IProyectosDelDesastre proyectos)
        : base(d, c, g, r)
    {
        _proyectos = proyectos;
    }

    protected override Guid IdDe(SolicitudTransicion s) => s.IdDesastre;

    protected override async Task AplicarAsync(Desastre desastre, SolicitudTransicion s, CancellationToken ct)
    {
        PerfilCicloVida perfil = await Perfiles.ExigirAsync(Catalogo, desastre.CodigoPerfil, ct);
        int abiertos = await _proyectos.NoFinalesAsync(desastre.Id, ct);
        desastre.CambiarEstado(s.Destino, s.Justificacion, perfil, abiertos, s.Actor, Reloj.Ahora);
    }
}

/// <summary>RF-GD-08 y RF-GD-09: nueva versión de la delimitación, completada con la capa de municipios. Publica EV-05.</summary>
public sealed class Delimitar : OperacionSobreDesastre<SolicitudDelimitar>, IDelimitarEntrada
{
    private readonly ICapaMunicipios _capa;

    public Delimitar(IRepositorioDesastres d, ICatalogo c, GuardadoDeDesastres g, IReloj r, ICapaMunicipios capa) : base(d, c, g, r)
    {
        _capa = capa;
    }

    protected override Guid IdDe(SolicitudDelimitar s) => s.IdDesastre;

    protected override async Task AplicarAsync(Desastre desastre, SolicitudDelimitar s, CancellationToken ct)
    {
        (string? geometria, IReadOnlyList<string> municipios) = await _capa.CompletarAsync(s.Geometria, s.Municipios, ct);
        desastre.Delimitar(geometria, municipios, s.Fuente, s.Justificacion, s.Actor, Reloj.Ahora);
    }
}

/// <summary>RF-GD-12: organismos participantes y responsable asignado. Publica EV-27.</summary>
public sealed class AsignarOrganismos : OperacionSobreDesastre<SolicitudOrganismos>, IAsignarOrganismosEntrada
{
    public AsignarOrganismos(IRepositorioDesastres d, ICatalogo c, GuardadoDeDesastres g, IReloj r) : base(d, c, g, r) { }
    protected override Guid IdDe(SolicitudOrganismos s) => s.IdDesastre;

    protected override Task AplicarAsync(Desastre desastre, SolicitudOrganismos s, CancellationToken ct)
    {
        desastre.AsignarOrganismos(s.Responsable, s.Participantes, s.Justificacion, s.Actor, Reloj.Ahora);
        return Task.CompletedTask;
    }
}

// -------------------------------------------------------------------- lecturas para operar (la consulta masiva es del servicio Consulta)

public sealed class ConsultarDesastre : Interactor, IConsultarDesastreEntrada
{
    private readonly IRepositorioDesastres _desastres;
    private readonly ICatalogo _catalogo;

    public ConsultarDesastre(IRepositorioDesastres desastres, ICatalogo catalogo)
    {
        _desastres = desastres;
        _catalogo = catalogo;
    }

    public Task EjecutarAsync(SolicitudDesastre s, IPuertoSalida<DesastreDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            Desastre? desastre = await _desastres.BuscarAsync(s.IdDesastre, ct);
            if (desastre is null)
            {
                salida.NoEncontrado($"No existe el desastre {s.IdDesastre}.");
                return;
            }
            salida.Exito(Mapeos.ADto(desastre, await _catalogo.PerfilAsync(desastre.CodigoPerfil, ct)));
        });
    }
}

/// <summary>Evolución de severidad, zona, estado y organismos, con autor e instante (Cuadro 15: /historial; RF-GD-17).</summary>
public sealed class ConsultarHistorial : Interactor, IConsultarHistorialEntrada
{
    private readonly IRepositorioDesastres _desastres;

    public ConsultarHistorial(IRepositorioDesastres desastres) { _desastres = desastres; }

    public Task EjecutarAsync(SolicitudDesastre s, IPuertoSalida<HistorialDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            Desastre? desastre = await _desastres.BuscarAsync(s.IdDesastre, ct);
            if (desastre is null)
            {
                salida.NoEncontrado($"No existe el desastre {s.IdDesastre}.");
                return;
            }
            IReadOnlyList<CambioDesastre> cambios = await _desastres.HistorialAsync(s.IdDesastre, ct);
            salida.Exito(new HistorialDto(s.IdDesastre, cambios.Select(Mapeos.ADto).ToList(), desastre.Zonas.Select(Mapeos.ADto).ToList()));
        });
    }
}
