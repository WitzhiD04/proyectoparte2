using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.CasosDeUso;

public sealed record SolicitudCandidatas(Actor Actor, Guid IdDesastre);
public interface IConsultarCandidatasEntrada : IPuertoEntrada<SolicitudCandidatas, IReadOnlyList<CandidataDto>> { }

/// <summary>CU-01. Evaluaciones del desastre que pueden originar un proyecto.</summary>
public sealed class ConsultarCandidatas : Interactor, IConsultarCandidatasEntrada
{
    private readonly ICopiasLocales _copias;
    private readonly IRepositorioProyectos _proyectos;

    public ConsultarCandidatas(ICopiasLocales copias, IRepositorioProyectos proyectos)
    {
        _copias = copias;
        _proyectos = proyectos;
    }

    public Task EjecutarAsync(SolicitudCandidatas solicitud, IPuertoSalida<IReadOnlyList<CandidataDto>> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAlgunRol(solicitud.Actor, Rol.Autoridad);
            DesastreLocal? desastre = await _copias.DesastreAsync(solicitud.IdDesastre, ct);
            if (desastre is null)
            {
                salida.NoEncontrado("El desastre no existe.");
                return;
            }
            if (desastre.Estado != EstadosDesastre.EnReconstruccion)
            {
                throw new ReglaDeNegocioException("DESASTRE_NO_EN_RECONSTRUCCION", "El desastre no está en reconstrucción.");
            }
            IReadOnlyList<EvaluacionValidada> evaluaciones = await _copias.EvaluacionesDeDesastreAsync(desastre.Id, ct);
            IReadOnlySet<Guid> vinculadas = await _proyectos.EvaluacionesVinculadasAsync(desastre.Id, ct);
            List<CandidataDto> candidatas = new();
            foreach (EvaluacionValidada e in CreacionDeProyectos.Candidatas(evaluaciones, vinculadas))
            {
                candidatas.Add(new CandidataDto(e.Id, e.IdActivo, e.NombreActivo, e.TipoActivo, e.NivelDano.ToString(), e.Municipio));
            }
            salida.Exito(candidatas);
        });
    }
}

public sealed record SolicitudLote(Actor Actor, Guid IdDesastre, IReadOnlyList<Guid> IdsEvaluaciones, string ClaveIdempotencia);
public interface ICrearProyectosPorLoteEntrada : IPuertoEntrada<SolicitudLote, ResultadoLoteDto> { }

/// <summary>CU-02. Crea un proyecto por activo. Si una evaluación falla no crea ninguno.
/// Reenviar el mismo lote devuelve el resultado original.</summary>
public sealed class CrearProyectosPorLote : Interactor, ICrearProyectosPorLoteEntrada
{
    private readonly ICopiasLocales _copias;
    private readonly IRepositorioProyectos _proyectos;
    private readonly GuardadoDeProyectos _guardado;
    private readonly IReloj _reloj;

    public CrearProyectosPorLote(ICopiasLocales copias, IRepositorioProyectos proyectos, GuardadoDeProyectos guardado, IReloj reloj)
    {
        _copias = copias;
        _proyectos = proyectos;
        _guardado = guardado;
        _reloj = reloj;
    }

    public Task EjecutarAsync(SolicitudLote solicitud, IPuertoSalida<ResultadoLoteDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            ExigirAlgunRol(solicitud.Actor, Rol.Autoridad);
            if (string.IsNullOrWhiteSpace(solicitud.ClaveIdempotencia))
            {
                throw new ReglaDeNegocioException("SIN_CLAVE", "El lote necesita una clave para reconocer reenvíos.");
            }

            LoteCreacion? anterior = await _proyectos.BuscarLoteAsync(solicitud.ClaveIdempotencia, ct);
            if (anterior is not null)
            {
                salida.Exito(new ResultadoLoteDto(anterior.IdsProyectos, true));
                return;
            }

            DesastreLocal? desastre = await _copias.DesastreAsync(solicitud.IdDesastre, ct);
            Dictionary<Guid, EvaluacionValidada> conocidas = new();
            HashSet<Guid> vinculadas = new();
            if (desastre is not null)
            {
                foreach (EvaluacionValidada e in await _copias.EvaluacionesDeDesastreAsync(desastre.Id, ct))
                {
                    conocidas[e.Id] = e;
                }
                vinculadas = new HashSet<Guid>(await _proyectos.EvaluacionesVinculadasAsync(desastre.Id, ct));
            }

            List<string> errores = CreacionDeProyectos.ValidarLote(desastre, solicitud.IdsEvaluaciones, conocidas, vinculadas);
            if (errores.Count > 0)
            {
                salida.ReglaIncumplida("LOTE_INVALIDO", "El lote no se creó porque tiene evaluaciones que no cumplen las reglas.", errores);
                return;
            }

            DateTimeOffset ahora = _reloj.Ahora;
            List<EvaluacionValidada> elegidas = new();
            foreach (Guid id in solicitud.IdsEvaluaciones)
            {
                elegidas.Add(conocidas[id]);
            }
            List<ProyectoReconstruccion> proyectos = CreacionDeProyectos.CrearProyectos(desastre!.Id, elegidas, solicitud.Actor, ahora);
            List<Guid> ids = new();
            foreach (ProyectoReconstruccion p in proyectos)
            {
                ids.Add(p.Id);
            }
            LoteCreacion lote = new(solicitud.ClaveIdempotencia, desastre.Id, solicitud.Actor.Usuario, ahora, ids);
            await _guardado.GuardarAsync(proyectos, lote, ct);
            salida.Exito(new ResultadoLoteDto(ids, false));
        });
    }
}
