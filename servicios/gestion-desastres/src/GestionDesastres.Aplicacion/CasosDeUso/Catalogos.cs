using GestionDesastres.Dominio;

namespace GestionDesastres.Aplicacion.CasosDeUso;

// Administración del catálogo en ejecución (AD-07, RN-1, RN-2, QA-10): incorporar un tipo o un perfil no modifica ni
// redespliega ningún componente. Rutas del SAD (Cuadro 15): /v1/catalogos/tipos-desastre y /{cod}/ciclo-vida. Las rutas de
// perfiles y de perfil por nivel son aditivas (no están en el Cuadro 15) y se declaran en ENMIENDAS_DOCUMENTOS.md.

public sealed record EntradaTipo(string Codigo, string Nombre, string Perfil, IReadOnlyDictionary<Severidad, string> Criterios,
    IReadOnlyDictionary<Severidad, IReadOnlyList<Participante>> Organismos, DateTimeOffset? VigenteDesde, DateTimeOffset? VigenteHasta);

public sealed record SolicitudGuardarTipo(Actor Actor, EntradaTipo Tipo, bool Nuevo);
public sealed record SolicitudCatalogo(Actor Actor);
public sealed record SolicitudPerfilDeTipo(Actor Actor, string CodigoTipo);
public sealed record SolicitudGuardarPerfil(Actor Actor, string Codigo, string Nombre, int Rango, IReadOnlyList<EstadoDesastre> Estados, bool Nuevo);
public sealed record SolicitudPerfilPorNivel(Actor Actor, IReadOnlyDictionary<Severidad, string> Minimos);

public interface IGuardarTipoEntrada : IPuertoEntrada<SolicitudGuardarTipo, TipoDesastreDto> { }
public interface IListarTiposEntrada : IPuertoEntrada<SolicitudCatalogo, IReadOnlyList<TipoDesastreDto>> { }
public interface IPerfilDeTipoEntrada : IPuertoEntrada<SolicitudPerfilDeTipo, PerfilDto> { }
public interface IListarPerfilesEntrada : IPuertoEntrada<SolicitudCatalogo, IReadOnlyList<PerfilDto>> { }
public interface IGuardarPerfilEntrada : IPuertoEntrada<SolicitudGuardarPerfil, PerfilDto> { }
public interface IConsultarPerfilPorNivelEntrada : IPuertoEntrada<SolicitudCatalogo, PerfilPorNivelDto> { }
public interface IGuardarPerfilPorNivelEntrada : IPuertoEntrada<SolicitudPerfilPorNivel, PerfilPorNivelDto> { }

public sealed class AdministracionCatalogo : Interactor,
    IGuardarTipoEntrada, IListarTiposEntrada, IPerfilDeTipoEntrada, IListarPerfilesEntrada, IGuardarPerfilEntrada,
    IConsultarPerfilPorNivelEntrada, IGuardarPerfilPorNivelEntrada
{
    private readonly ICatalogo _catalogo;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IReloj _reloj;

    public AdministracionCatalogo(ICatalogo catalogo, IUnidadDeTrabajo unidad, IReloj reloj)
    {
        _catalogo = catalogo;
        _unidad = unidad;
        _reloj = reloj;
    }

    private static void ExigirAdministrador(Actor actor)
    {
        if (!actor.EsAdministrador)
        {
            throw new NoAutorizadoException("Solo el administrador de la plataforma modifica el catálogo.");
        }
    }

    public Task EjecutarAsync(SolicitudGuardarTipo s, IPuertoSalida<TipoDesastreDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAdministrador(s.Actor);
            EntradaTipo e = s.Tipo;
            if (await _catalogo.PerfilAsync(e.Perfil, ct) is null)
            {
                throw new ReglaDeNegocioException("PERFIL_DESCONOCIDO", $"El perfil {e.Perfil} no está en el catálogo.");
            }
            TipoDesastre? existente = await _catalogo.TipoAsync(e.Codigo, ct);
            if (s.Nuevo && existente is not null)
            {
                throw new ReglaDeNegocioException("TIPO_EXISTE", $"El tipo {e.Codigo} ya existe; su código es inmutable.");
            }
            if (!s.Nuevo && existente is null)
            {
                salida.NoEncontrado($"No existe el tipo {e.Codigo}.");
                return;
            }
            DateTimeOffset desde = e.VigenteDesde ?? existente?.VigenteDesde ?? _reloj.Ahora;
            TipoDesastre tipo = existente ?? new TipoDesastre(e.Codigo, e.Nombre, e.Perfil, e.Criterios, e.Organismos, desde, e.VigenteHasta);
            if (existente is not null)
            {
                tipo.Modificar(e.Nombre, e.Perfil, e.Criterios, e.Organismos, desde, e.VigenteHasta);
            }
            await _catalogo.GuardarTipoAsync(tipo, s.Nuevo, ct);
            await _unidad.ConfirmarAsync(ct);
            salida.Exito(Mapeos.ADto(tipo, _reloj.Ahora));
        });
    }

    public Task EjecutarAsync(SolicitudCatalogo s, IPuertoSalida<IReadOnlyList<TipoDesastreDto>> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            DateTimeOffset ahora = _reloj.Ahora;
            salida.Exito((await _catalogo.TiposAsync(ct)).Select(t => Mapeos.ADto(t, ahora)).ToList());
        });
    }

    public Task EjecutarAsync(SolicitudPerfilDeTipo s, IPuertoSalida<PerfilDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            TipoDesastre? tipo = await _catalogo.TipoAsync(s.CodigoTipo, ct);
            PerfilCicloVida? perfil = tipo is null ? null : await _catalogo.PerfilAsync(tipo.CodigoPerfil, ct);
            if (perfil is null)
            {
                salida.NoEncontrado($"No existe el tipo {s.CodigoTipo} o su perfil.");
                return;
            }
            salida.Exito(Mapeos.ADto(perfil));
        });
    }

    public Task EjecutarAsync(SolicitudCatalogo s, IPuertoSalida<IReadOnlyList<PerfilDto>> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () => salida.Exito((await _catalogo.PerfilesAsync(ct)).Select(Mapeos.ADto).ToList()));
    }

    public Task EjecutarAsync(SolicitudGuardarPerfil s, IPuertoSalida<PerfilDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAdministrador(s.Actor);
            PerfilCicloVida perfil = new(s.Codigo, s.Nombre, s.Rango, s.Estados);
            PerfilCicloVida? existente = await _catalogo.PerfilAsync(perfil.Codigo, ct);
            if (s.Nuevo && existente is not null)
            {
                throw new ReglaDeNegocioException("PERFIL_EXISTE", $"El perfil {perfil.Codigo} ya existe.");
            }
            if (!s.Nuevo && existente is null)
            {
                salida.NoEncontrado($"No existe el perfil {perfil.Codigo}.");
                return;
            }
            // Un perfil que ya usan desastres no puede perder estados: dejaría eventos en un estado que su perfil no tiene.
            if (existente is not null && existente.Estados.Except(perfil.Estados).Any())
            {
                throw new ReglaDeNegocioException("PERFIL_EN_USO", "Un perfil existente solo puede ganar estados, no perderlos.");
            }
            await _catalogo.GuardarPerfilAsync(perfil, s.Nuevo, ct);
            await _unidad.ConfirmarAsync(ct);
            salida.Exito(Mapeos.ADto(perfil));
        });
    }

    public Task EjecutarAsync(SolicitudCatalogo s, IPuertoSalida<PerfilPorNivelDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            PerfilPorNivel valor = await _catalogo.PerfilPorNivelAsync(ct);
            salida.Exito(new PerfilPorNivelDto(valor.Minimos.ToDictionary(p => p.Key.ToString(), p => p.Value)));
        });
    }

    public Task EjecutarAsync(SolicitudPerfilPorNivel s, IPuertoSalida<PerfilPorNivelDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAdministrador(s.Actor);
            PerfilPorNivel valor = new(s.Minimos);
            foreach (string codigo in valor.Minimos.Values.Distinct())
            {
                if (await _catalogo.PerfilAsync(codigo, ct) is null)
                {
                    throw new ReglaDeNegocioException("PERFIL_DESCONOCIDO", $"El perfil {codigo} no está en el catálogo.");
                }
            }
            await _catalogo.GuardarPerfilPorNivelAsync(valor, ct);
            await _unidad.ConfirmarAsync(ct);
            salida.Exito(new PerfilPorNivelDto(valor.Minimos.ToDictionary(p => p.Key.ToString(), p => p.Value)));
        });
    }
}
