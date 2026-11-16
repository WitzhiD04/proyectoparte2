using ConsultaDanos.Dominio;

namespace ConsultaDanos.Aplicacion;

/// <summary>
/// Aplica al modelo de lectura un hecho que llegó del bus (AD-34). Los eventos pueden llegar repetidos y en cualquier
/// orden, porque dos réplicas consumen la misma cola: cada aspecto se protege con su versión (reglas en
/// <see cref="EstadoActivo"/>), y el resultado final no depende del orden. Al final programa la invalidación del mapa de
/// los desastres que cambiaron (E-21).
/// </summary>
public sealed class ActualizarProyecciones
{
    private readonly IAlmacenProyecciones _almacen;
    private readonly IInvalidacionMapa _invalidacion;

    public ActualizarProyecciones(IAlmacenProyecciones almacen, IInvalidacionMapa invalidacion)
    {
        _almacen = almacen;
        _invalidacion = invalidacion;
    }

    /// <summary>EV-09 y EV-26: el contenido de una evaluación.</summary>
    public async Task AplicarAsync(EvaluacionRegistrada h, CancellationToken ct)
    {
        EstadoActivo antes = await _almacen.BloquearActivoAsync(h.IdActivo, h.Instante, ct);
        EstadoValidacion? validacion = await _almacen.ValidacionDeAsync(h.Contenido.IdEvaluacion, ct);
        EstadoActivo despues = antes.ConContenido(h.Contenido, h.VersionActivo, h.Origen, _ => validacion);
        await GuardarAsync([(antes, despues)], h.Instante, ct);
        await _almacen.RegistrarHistorialAsync(Entrada(h.IdActivo, h.Contenido, h.Motivo, h.Instante, null, h.EnConflicto, h.Autor, null), ct);
    }

    /// <summary>EV-08: una validación entra al consolidado solo si es VALIDADA; si vuelve a pendiente o se rechaza, sale.</summary>
    public async Task AplicarAsync(ValidacionCambiada h, CancellationToken ct)
    {
        EstadoActivo antes = await _almacen.BloquearActivoAsync(h.IdActivo, h.Instante, ct);
        await _almacen.GuardarValidacionAsync(h, ct);
        EstadoValidacion? vigente = await _almacen.ValidacionDeAsync(h.Contenido.IdEvaluacion, ct);
        // Si EV-08 llegó antes que la evaluación, su copia del contenido sirve hasta que llegue EV-09 (versión 0).
        EstadoActivo despues = antes
            .ConContenido(h.Contenido, 0, OrigenContenido.Validacion, _ => vigente)
            .ConValidacion(h.Contenido.IdEvaluacion, vigente);
        await GuardarAsync([(antes, despues)], h.Instante, ct);
        await _almacen.RegistrarHistorialAsync(Entrada(h.IdActivo, h.Contenido, OrigenHistorial.Validacion, h.InstanteValidacion,
            h.Estado, false, h.ValidadoPor, null), ct);
    }

    /// <summary>EV-10: el conflicto marca el activo en el mapa y entra en los indicadores de R-08 (E-13).</summary>
    public async Task AplicarAsync(ConflictoDetectado h, CancellationToken ct)
    {
        await _almacen.BloquearBandejaAsync(ct);
        ConflictoLeido conflicto = await _almacen.GuardarConflictoDetectadoAsync(h, ct);
        if (conflicto.FusionaDuplicado)
        {
            // EV-22 llegó antes: recién ahora se sabe qué activo era el duplicado.
            EstadoActivo antes = await _almacen.BloquearActivoAsync(conflicto.IdActivo!.Value, h.Instante, ct);
            await GuardarAsync([(antes, antes.FusionarEn(conflicto.IdActivoVigente!.Value))], h.Instante, ct);
        }
        await _almacen.RecalcularBandejaAsync(h.Instante, ct);
        await InvalidarMarcaAsync(h.IdActivo, h.Instante, ct);
    }

    /// <summary>
    /// EV-22: el mapa refleja la evaluación que prevalece (AD-34) y el conflicto sale de la bandeja. Si se fusionó un
    /// duplicado, su activo deja de contar: es el mismo activo que el vigente (E-14).
    /// </summary>
    public async Task AplicarAsync(ConflictoResuelto h, CancellationToken ct)
    {
        await _almacen.BloquearBandejaAsync(ct);
        ConflictoLeido conflicto = await _almacen.GuardarConflictoResueltoAsync(h, ct);

        // Los activos se bloquean antes de escribir y siempre en el mismo orden, para no interbloquearse con otra réplica.
        SortedSet<Guid> ids = [h.IdActivoVigente];
        if (conflicto.FusionaDuplicado) ids.Add(conflicto.IdActivo!.Value);
        Dictionary<Guid, EstadoActivo> antes = [];
        foreach (Guid id in ids)
        {
            antes[id] = await _almacen.BloquearActivoAsync(id, h.Instante, ct);
        }

        EstadoValidacion? validacion = await _almacen.ValidacionDeAsync(h.Vigente.IdEvaluacion, ct);
        List<(EstadoActivo, EstadoActivo)> transiciones =
        [
            (antes[h.IdActivoVigente],
             antes[h.IdActivoVigente].ConContenido(h.Vigente, h.VersionActivo, OrigenContenido.Resolucion, _ => validacion)),
        ];
        if (conflicto.FusionaDuplicado)
        {
            EstadoActivo duplicado = antes[conflicto.IdActivo!.Value];
            transiciones.Add((duplicado, duplicado.FusionarEn(h.IdActivoVigente)));
        }
        await GuardarAsync(transiciones, h.Instante, ct);
        await _almacen.RegistrarHistorialAsync(Entrada(h.IdActivoVigente, h.Vigente, OrigenHistorial.Resolucion, h.Instante, null, false,
            h.DecididoPor, h.Decision), ct);
        await _almacen.RecalcularBandejaAsync(h.Instante, ct);

        // El activo del conflicto deja de estar marcado: su mapa cambia aunque su contenido no.
        if (conflicto.IdActivo is Guid idActivoConflicto)
        {
            await InvalidarMarcaAsync(idActivoConflicto, h.Instante, ct);
        }
    }

    /// <summary>EV-21: una capa nueva cambia las geometrías de todos los mapas (AD-17, E-21).</summary>
    public async Task AplicarAsync(CapaRecibida h, CancellationToken ct)
    {
        await _almacen.GuardarCapaAsync(h, ct);
        _invalidacion.ProgramarGlobal();
    }

    /// <summary>EV-25: el nombre de un tipo aparece en el consolidado.</summary>
    public async Task AplicarAsync(TipoActivoActualizado h, CancellationToken ct)
    {
        await _almacen.GuardarTipoActivoAsync(h, ct);
        _invalidacion.ProgramarGlobal();
    }

    /// <summary>EV-23: el consolidado muestra qué integraciones están caídas (R-10).</summary>
    public async Task AplicarAsync(EstadoIntegracion h, CancellationToken ct)
    {
        await _almacen.GuardarIntegracionAsync(h, ct);
        _invalidacion.ProgramarGlobal();
    }

    /// <summary>Guarda las transiciones que cambian algo y programa la invalidación de los desastres afectados.</summary>
    private async Task GuardarAsync(IReadOnlyList<(EstadoActivo Antes, EstadoActivo Despues)> transiciones, DateTimeOffset instante,
        CancellationToken ct)
    {
        List<(EstadoActivo Antes, EstadoActivo Despues)> cambios = transiciones.Where(t => t.Antes != t.Despues).ToList();
        if (cambios.Count == 0)
        {
            return;
        }
        List<Guid> desastres = cambios.SelectMany(t => Consolidacion.DesastresAfectados(t.Antes, t.Despues)).Distinct().Order().ToList();
        await _almacen.GuardarActivosAsync(cambios.Select(t => t.Despues).ToList(), Consolidacion.Deltas(cambios), desastres, instante, ct);
        _invalidacion.Programar(desastres);
    }

    /// <summary>La marca de conflicto se calcula al consultar: basta con invalidar el mapa del desastre del activo.</summary>
    private async Task InvalidarMarcaAsync(Guid idActivo, DateTimeOffset instante, CancellationToken ct)
    {
        if (await _almacen.DesastreDelActivoAsync(idActivo, ct) is Guid desastre)
        {
            await _almacen.MarcarDesastreAsync(desastre, instante, ct);
            _invalidacion.Programar([desastre]);
        }
    }

    private static EntradaHistorial Entrada(Guid idActivo, ContenidoActivo c, string origen, DateTimeOffset instante,
        EstadoValidacion? validacion, bool enConflicto, string? autor, string? detalle) =>
        new(idActivo, c.IdEvaluacion, c.VersionEvaluacion, origen, instante, c.Nivel, c.TipoActivo, c.EstadoEvaluacion, validacion,
            enConflicto, c.IdDesastre, c.CodigoMunicipio, autor, detalle);
}
