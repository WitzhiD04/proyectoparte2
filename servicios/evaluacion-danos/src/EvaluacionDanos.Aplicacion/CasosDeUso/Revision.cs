using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

public sealed record SolicitudValidacion(Guid IdEvaluacion, string? Estado, string? Motivo, Actor Actor);

/// <summary>
/// Validación de una evaluación (RF-ED-08, DA-09, corrige H-10): la marca como validada, rechazada o pendiente,
/// registra quién lo hizo y cuándo, conserva la versión anterior (AD-30) y publica EV-08 en cada cambio (E-02).
/// </summary>
public sealed class ValidarEvaluacion(IAlmacenEvaluaciones almacen, IPublicadorHechos hechos, ICodificador codificador,
    IReloj reloj) : ICasoDeUso<SolicitudValidacion, EvaluacionDetalleDto>
{
    public async Task<Resultado<EvaluacionDetalleDto>> EjecutarAsync(SolicitudValidacion solicitud, CancellationToken cancelar = default)
    {
        if (solicitud.Estado is null || !ValidadorEvaluacion.EsNombreDeEnum<EstadoValidacion>(solicitud.Estado))
        {
            return Resultado<EvaluacionDetalleDto>.Falla(TipoFallo.Invalido,
                $"estado '{solicitud.Estado}' no válido (VALIDADA, RECHAZADA o PENDIENTE)");
        }
        var nuevo = Enum.Parse<EstadoValidacion>(solicitud.Estado);

        var evaluacion = await almacen.EvaluacionAsync(solicitud.IdEvaluacion, cancelar);
        if (evaluacion is null)
        {
            return Resultado<EvaluacionDetalleDto>.Falla(TipoFallo.NoEncontrado, $"No existe la evaluación {solicitud.IdEvaluacion}");
        }
        var desastre = evaluacion.DesastreId is Guid idDesastre ? await almacen.DesastreAsync(idDesastre, cancelar) : null;
        bool conflictoPendiente = await almacen.TieneConflictoPendienteAsync(evaluacion.Id, cancelar);

        // Regla de nivel 2 (AD-22): la organización del usuario participa en el desastre (EV-27).
        var rechazo = ReglasRevision.Validacion(evaluacion, desastre, conflictoPendiente, solicitud.Actor.Organizacion,
            nuevo, solicitud.Motivo);
        if (rechazo != null)
        {
            return Resultado<EvaluacionDetalleDto>.De(rechazo);
        }

        var activo = await almacen.ActivoAsync(evaluacion.ActivoId, cancelar)
            ?? throw new InvalidOperationException($"La evaluación {evaluacion.Id} no tiene su activo {evaluacion.ActivoId}");
        var ahora = reloj.Ahora();
        string usuario = solicitud.Actor.Usuario;

        evaluacion.Estado = ReglasRevision.EstadoTras(nuevo);
        evaluacion.ValidadoPor = CambioEnServidor.Recortar(usuario, Limites.Nombre);
        evaluacion.InstanteValidacion = ahora;

        string detalle = string.IsNullOrWhiteSpace(solicitud.Motivo) ? nuevo.ToString() : $"{nuevo}: {solicitud.Motivo.Trim()}";
        int numero = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, evaluacion, MotivoVersion.VALIDACION,
            usuario, detalle, ahora, cancelar);
        var vigente = await almacen.EvaluacionVigenteDelActivoAsync(activo.Id, cancelar);
        CambioEnServidor.SubirVersionDelActivo(almacen, activo, ActivoVersion.AutorServidor(usuario),
            vigente == evaluacion.Id ? evaluacion.Id : null, ahora);

        hechos.Registrar(new ValidacionCambiada(InstantaneaEvaluacion.De(evaluacion, activo, numero), desastre!.Id,
            evaluacion.Evaluador, evaluacion.Organizacion, CambioEnServidor.Referencia(activo), nuevo, usuario, ahora));

        return await CambioEnServidor.GuardarAsync<EvaluacionDetalleDto>(almacen, cancelar)
            ?? Resultado<EvaluacionDetalleDto>.Ok(CambioEnServidor.Detalle(evaluacion, activo, numero));
    }
}

public sealed record SolicitudResolucion(Guid IdConflicto, string? Decision, string? Justificacion, Actor Actor);

/// <summary>
/// Resolución humana de un conflicto o de un posible duplicado (AD-32, AD-33, RF-ED-09, corrige H-11). La plataforma
/// detecta y conserva; aquí decide un usuario autorizado. Ninguna versión ni evidencia se elimina: la decisión crea
/// versiones nuevas y deja escrito quién decidió, qué y por qué. Publica EV-22 (E-05).
/// <list type="bullet">
/// <item>PREVALECE_ENTRANTE: el contenido en conflicto pasa a ser el de la evaluación entrante, que queda vigente.</item>
/// <item>PREVALECE_VIGENTE: la vigente sigue; la entrante vuelve a su estado anterior o, si nació en conflicto, queda rechazada.</item>
/// <item>FUSIONAR (E-14): la entrante queda como DUPLICADO de la vigente y sus evidencias se vinculan también a ella.</item>
/// <item>RECHAZAR_DUPLICADO: no eran el mismo activo; la entrante queda como una evaluación normal de su activo.</item>
/// </list>
/// </summary>
public sealed class ResolverConflicto(IAlmacenEvaluaciones almacen, IPublicadorHechos hechos, ICodificador codificador,
    IReloj reloj) : ICasoDeUso<SolicitudResolucion, ResolucionDto>
{
    public async Task<Resultado<ResolucionDto>> EjecutarAsync(SolicitudResolucion solicitud, CancellationToken cancelar = default)
    {
        if (solicitud.Decision is null || !ValidadorEvaluacion.EsNombreDeEnum<DecisionConflicto>(solicitud.Decision))
        {
            return Resultado<ResolucionDto>.Falla(TipoFallo.Invalido, $"decision '{solicitud.Decision}' no válida " +
                "(PREVALECE_ENTRANTE, PREVALECE_VIGENTE, FUSIONAR o RECHAZAR_DUPLICADO)");
        }
        var decision = Enum.Parse<DecisionConflicto>(solicitud.Decision);

        var conflicto = await almacen.ConflictoAsync(solicitud.IdConflicto, cancelar);
        if (conflicto is null)
        {
            return Resultado<ResolucionDto>.Falla(TipoFallo.NoEncontrado, $"No existe el conflicto {solicitud.IdConflicto}");
        }
        var rechazo = ReglasRevision.Resolucion(conflicto, decision, conflicto.EvaluacionVigenteId is not null, solicitud.Justificacion);
        if (rechazo != null)
        {
            return Resultado<ResolucionDto>.De(rechazo);
        }

        var entrante = await Exigir(almacen.EvaluacionAsync(conflicto.EvaluacionEntranteId, cancelar), "la evaluación entrante");
        var vigente = conflicto.EvaluacionVigenteId is Guid idVigente
            ? await Exigir(almacen.EvaluacionAsync(idVigente, cancelar), "la evaluación vigente")
            : null;
        // Si la evaluación tiene otro conflicto sin resolver, sigue en conflicto: esta decisión no lo resuelve.
        bool otroPendiente = await almacen.TieneConflictoPendienteComoEntranteAsync(entrante.Id, conflicto.Id, cancelar);

        var ahora = reloj.Ahora();
        string usuario = solicitud.Actor.Usuario;
        string justificacion = solicitud.Justificacion!.Trim();
        string detalle = $"{decision}: {justificacion}";
        Evaluacion prevalece;
        int versionPrevalece;

        switch (decision)
        {
            case DecisionConflicto.PREVALECE_ENTRANTE:
            {
                var versionEnConflicto = await Exigir(almacen.VersionAsync(conflicto.VersionEntranteId, cancelar), "la versión en conflicto");
                var activo = await Exigir(almacen.ActivoAsync(entrante.ActivoId, cancelar), "el activo");
                var estadoPrevio = conflicto.EstadoPrevioEntrante;
                var desastrePrevio = entrante.DesastreId;

                // El contenido que quedó guardado en la versión en conflicto pasa a ser el de la evaluación.
                AplicarContenido(codificador.Deserializar<EvaluacionEntrante>(versionEnConflicto.ContenidoJson), entrante, ahora);
                entrante.Estado = otroPendiente ? EstadoEvaluacion.EN_CONFLICTO : entrante.EstadoSinRevision();
                versionPrevalece = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, entrante, MotivoVersion.RESOLUCION,
                    usuario, detalle, ahora, cancelar, versionEnConflicto.ContenidoJson);
                // La versión nueva del activo es del dispositivo que hizo la evaluación: sus operaciones siguientes
                // no chocan con su propio contenido, y las de los demás dispositivos sí (AD-43).
                CambioEnServidor.SubirVersionDelActivo(almacen, activo, entrante.IdDispositivo, entrante.Id, ahora);

                if (ReglasRevision.TeniaValidacion(estadoPrevio) && desastrePrevio is Guid idDesastre)
                {
                    entrante.ValidadoPor = CambioEnServidor.Recortar(usuario, Limites.Nombre);
                    entrante.InstanteValidacion = ahora;
                    hechos.Registrar(new ValidacionCambiada(InstantaneaEvaluacion.De(entrante, activo, versionPrevalece), idDesastre,
                        entrante.Evaluador, entrante.Organizacion, CambioEnServidor.Referencia(activo), EstadoValidacion.PENDIENTE,
                        usuario, ahora));
                }
                prevalece = entrante;
                break;
            }
            case DecisionConflicto.PREVALECE_VIGENTE:
            {
                bool seAplicoAntes = await almacen.TieneVersionAplicadaAsync(entrante.Id, cancelar);
                entrante.Estado = otroPendiente ? EstadoEvaluacion.EN_CONFLICTO
                    : conflicto.EstadoPrevioEntrante ?? (seAplicoAntes ? entrante.EstadoSinRevision() : EstadoEvaluacion.RECHAZADA);
                int versionEntrante = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, entrante,
                    MotivoVersion.RESOLUCION, usuario, detalle, ahora, cancelar);
                // La entrante y la vigente pueden ser la misma evaluación (su dueño la editó sobre una base anterior
                // a un cambio hecho en el servidor): entonces basta una versión.
                versionPrevalece = vigente!.Id == entrante.Id
                    ? versionEntrante
                    : await CambioEnServidor.NuevaVersionAsync(almacen, codificador, vigente, MotivoVersion.RESOLUCION,
                        usuario, detalle, ahora, cancelar);
                prevalece = vigente;
                break;
            }
            case DecisionConflicto.FUSIONAR:
            {
                entrante.Estado = EstadoEvaluacion.DUPLICADO;
                entrante.DuplicadoDe = vigente!.Id;
                foreach (Guid idEvidencia in await almacen.IdsEvidenciasAsync(entrante.Id, cancelar))
                {
                    almacen.Agregar(new EvidenciaVinculada
                    {
                        EvaluacionId = vigente.Id, EvidenciaId = idEvidencia, ConflictoId = conflicto.Id, Instante = ahora
                    });
                }
                // El activo duplicado no se borra: se anota en cuál quedó y deja de ofrecerse en la descarga.
                var activoDuplicado = await Exigir(almacen.ActivoAsync(entrante.ActivoId, cancelar), "el activo duplicado");
                activoDuplicado.FusionadoEn = vigente.ActivoId;
                CambioEnServidor.SubirVersionDelActivo(almacen, activoDuplicado, ActivoVersion.AutorServidor(usuario), null, ahora);

                await CambioEnServidor.NuevaVersionAsync(almacen, codificador, entrante, MotivoVersion.RESOLUCION, usuario,
                    $"{detalle} (duplicado de {vigente.Id})", ahora, cancelar);
                versionPrevalece = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, vigente, MotivoVersion.RESOLUCION,
                    usuario, $"{detalle} (absorbe a {entrante.Id})", ahora, cancelar);
                prevalece = vigente;
                break;
            }
            default:   // RECHAZAR_DUPLICADO
            {
                entrante.Estado = otroPendiente ? EstadoEvaluacion.EN_CONFLICTO : entrante.EstadoSinRevision();
                versionPrevalece = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, entrante, MotivoVersion.RESOLUCION,
                    usuario, detalle, ahora, cancelar);
                prevalece = entrante;
                break;
            }
        }

        // Estado es token de concurrencia: si dos operadores deciden a la vez, el segundo recibe un 409.
        conflicto.Estado = EstadoConflicto.RESUELTO;
        conflicto.Decision = decision;
        conflicto.DecididoPor = CambioEnServidor.Recortar(usuario, Limites.Nombre);
        conflicto.Justificacion = justificacion;
        conflicto.InstanteResolucion = ahora;

        var activoPrevalece = await Exigir(almacen.ActivoAsync(prevalece.ActivoId, cancelar), "el activo de la evaluación vigente");
        var instantanea = InstantaneaEvaluacion.De(prevalece, activoPrevalece, versionPrevalece);
        hechos.Registrar(new ConflictoResuelto(conflicto.Id, decision, usuario, justificacion, instantanea, ahora));

        return await CambioEnServidor.GuardarAsync<ResolucionDto>(almacen, cancelar)
            ?? Resultado<ResolucionDto>.Ok(new ResolucionDto(conflicto.Id, decision,
                CambioEnServidor.Detalle(prevalece, activoPrevalece, versionPrevalece)));
    }

    /// <summary>Igual que al aplicar una operación, pero sin cambiar el dueño ni el desfase de reloj de la evaluación.</summary>
    private static void AplicarContenido(EvaluacionEntrante e, Evaluacion destino, DateTime ahora)
    {
        destino.DesastreId = e.DesastreId;
        destino.InstanteCampo = e.InstanteCampo!.Value.UtcDateTime;
        destino.Evaluador = e.Evaluador!.Trim();
        destino.Organizacion = e.Organizacion!.Trim();
        destino.Lat = e.Lat!.Value;
        destino.Lon = e.Lon!.Value;
        destino.TipoActivoCodigo = e.TipoActivoCodigo!;
        destino.NivelDano = Enum.Parse<NivelDano>(e.NivelDano!);
        destino.Descripcion = e.Descripcion!;
        destino.PrecisionM = e.PrecisionM!.Value;
        destino.InstanteServidor = ahora;
    }

    private static async Task<T> Exigir<T>(Task<T?> lectura, string que) where T : class =>
        await lectura ?? throw new InvalidOperationException($"El conflicto apunta a {que}, que no existe");
}
