using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

public sealed record SolicitudRegistro(Operacion? Operacion, Actor Actor);

/// <summary>
/// Registro de una evaluación con conexión (RF-ED-01, AD-29, corrige H-17). Es un lote de una sola operación por el
/// mismo camino que la sincronización: mismas validaciones, misma regla de conflicto, mismo detector de duplicados y
/// mismo reenvío idempotente. Así no hay dos maneras de crear una evaluación.
/// </summary>
public sealed class RegistrarEvaluacion(SincronizarLote sincronizar) : ICasoDeUso<SolicitudRegistro, ResultadoOperacionDto>
{
    public async Task<Resultado<ResultadoOperacionDto>> EjecutarAsync(SolicitudRegistro solicitud, CancellationToken cancelar = default)
    {
        var instanteRecepcion = DateTime.UtcNow;
        var operacion = solicitud.Operacion;
        if (operacion?.Evaluacion is null)
        {
            return Resultado<ResultadoOperacionDto>.Falla(TipoFallo.Invalido, "Falta la evaluación");
        }
        operacion.Tipo ??= nameof(TipoOperacion.CREAR);
        // Sin idOperacion, el id de la evaluación identifica el registro: repetir la petición devuelve lo mismo.
        operacion.IdOperacion ??= operacion.Evaluacion.Id;

        // Desde la app el dueño es el dispositivo; desde la consola, el usuario.
        string dispositivo = solicitud.Actor.IdDispositivo ?? ActivoVersion.AutorServidor(solicitud.Actor.Usuario);
        if (dispositivo.Length > Limites.IdDispositivo)
        {
            return Resultado<ResultadoOperacionDto>.Falla(TipoFallo.Invalido, $"El dispositivo supera los {Limites.IdDispositivo} caracteres");
        }
        var lote = new LoteSincronizacion { IdDispositivo = dispositivo, Operaciones = [operacion] };
        try
        {
            var resultados = await sincronizar.ProcesarLoteAsync(lote, instanteRecepcion, cancelar);
            return Resultado<ResultadoOperacionDto>.Ok(resultados[0]);
        }
        catch (Exception ex) when (ex is LoteIncompletoException or CapaNoDisponibleException)
        {
            return Resultado<ResultadoOperacionDto>.Falla(TipoFallo.NoDisponible, ex.Message);
        }
    }
}

/// <summary>Los atributos que se pueden cambiar; los que no llegan quedan como están.</summary>
public sealed record CambiosEvaluacion(string? NivelDano, string? Descripcion, double? Lat, double? Lon, double? PrecisionM,
    DateTimeOffset? InstanteCampo);

public sealed record SolicitudActualizacion(Guid IdEvaluacion, CambiosEvaluacion? Cambios, Actor Actor);

/// <summary>
/// Actualización de una evaluación con conexión (RF-ED-07): conserva la versión anterior con su autor y su instante
/// (AD-30) y publica EV-26 (E-06). El contenido resultante pasa por el mismo validador que una operación de la app.
/// </summary>
public sealed class ActualizarEvaluacion(IAlmacenEvaluaciones almacen, IPublicadorHechos hechos, ICodificador codificador,
    IReloj reloj) : ICasoDeUso<SolicitudActualizacion, EvaluacionDetalleDto>
{
    public async Task<Resultado<EvaluacionDetalleDto>> EjecutarAsync(SolicitudActualizacion solicitud, CancellationToken cancelar = default)
    {
        var cambios = solicitud.Cambios;
        if (cambios is null || cambios == new CambiosEvaluacion(null, null, null, null, null, null))
        {
            return Falla(TipoFallo.Invalido, "La actualización no trae ningún cambio");
        }
        var evaluacion = await almacen.EvaluacionAsync(solicitud.IdEvaluacion, cancelar);
        if (evaluacion is null)
        {
            return Falla(TipoFallo.NoEncontrado, $"No existe la evaluación {solicitud.IdEvaluacion}");
        }
        // Regla de nivel 2 (AD-22): el evaluador solo actualiza lo que registró su dispositivo; el operador, cualquiera.
        var actor = solicitud.Actor;
        if (!actor.EsOperador && evaluacion.IdDispositivo != (actor.IdDispositivo ?? ActivoVersion.AutorServidor(actor.Usuario)))
        {
            return Falla(TipoFallo.NoAutorizado, $"La evaluación {evaluacion.Id} la registró otro dispositivo");
        }
        if (evaluacion.Estado == EstadoEvaluacion.DUPLICADO)
        {
            return Falla(TipoFallo.ReglaIncumplida, $"La evaluación se fusionó como duplicado de {evaluacion.DuplicadoDe}; actualice la vigente");
        }
        if (evaluacion.Estado == EstadoEvaluacion.EN_CONFLICTO || await almacen.TieneConflictoPendienteAsync(evaluacion.Id, cancelar))
        {
            return Falla(TipoFallo.ReglaIncumplida, "La evaluación tiene un conflicto pendiente de revisión; resuélvalo antes de actualizarla");
        }

        var contenido = evaluacion.ComoContenido();
        contenido.NivelDano = cambios.NivelDano ?? contenido.NivelDano;
        contenido.Descripcion = cambios.Descripcion ?? contenido.Descripcion;
        contenido.Lat = cambios.Lat ?? contenido.Lat;
        contenido.Lon = cambios.Lon ?? contenido.Lon;
        contenido.PrecisionM = cambios.PrecisionM ?? contenido.PrecisionM;
        contenido.InstanteCampo = cambios.InstanteCampo ?? contenido.InstanteCampo;

        var (tipos, desastres) = await almacen.CatalogoAsync(cancelar);
        string? motivo = ValidadorEvaluacion.Validar(new Operacion
        {
            IdOperacion = Guid.NewGuid(), Tipo = nameof(TipoOperacion.ACTUALIZAR), Evaluacion = contenido
        }, tipos, desastres);
        if (motivo != null)
        {
            return Falla(TipoFallo.Invalido, motivo);
        }

        var activo = await almacen.ActivoAsync(evaluacion.ActivoId, cancelar)
            ?? throw new InvalidOperationException($"La evaluación {evaluacion.Id} no tiene su activo {evaluacion.ActivoId}");
        var ahora = reloj.Ahora();
        var estadoPrevio = evaluacion.Estado;

        evaluacion.NivelDano = Enum.Parse<NivelDano>(contenido.NivelDano!);
        evaluacion.Descripcion = contenido.Descripcion!;
        evaluacion.Lat = contenido.Lat!.Value;
        evaluacion.Lon = contenido.Lon!.Value;
        evaluacion.PrecisionM = contenido.PrecisionM!.Value;
        evaluacion.InstanteCampo = contenido.InstanteCampo!.Value.UtcDateTime;
        evaluacion.InstanteServidor = ahora;
        // Lo validado (o rechazado) era el contenido anterior: vuelve a quedar pendiente de validación.
        bool teniaValidacion = ReglasRevision.TeniaValidacion(estadoPrevio);
        if (teniaValidacion)
        {
            evaluacion.Estado = evaluacion.EstadoSinRevision();
            evaluacion.ValidadoPor = CambioEnServidor.Recortar(actor.Usuario, Limites.Nombre);
            evaluacion.InstanteValidacion = ahora;
        }

        int numero = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, evaluacion, MotivoVersion.ACTUALIZACION,
            actor.Usuario, null, ahora, cancelar);
        var vigente = await almacen.EvaluacionVigenteDelActivoAsync(activo.Id, cancelar);
        CambioEnServidor.SubirVersionDelActivo(almacen, activo, ActivoVersion.AutorServidor(actor.Usuario),
            vigente == evaluacion.Id ? evaluacion.Id : null, ahora);

        var instantanea = InstantaneaEvaluacion.De(evaluacion, activo, numero);
        hechos.Registrar(new EvaluacionActualizada(instantanea, MotivoActualizacion.ACTUALIZACION, actor.Usuario, ahora));
        if (teniaValidacion && evaluacion.DesastreId is Guid idDesastre)
        {
            hechos.Registrar(new ValidacionCambiada(instantanea, idDesastre, evaluacion.Evaluador, evaluacion.Organizacion,
                CambioEnServidor.Referencia(activo), EstadoValidacion.PENDIENTE, actor.Usuario, ahora));
        }

        return await CambioEnServidor.GuardarAsync<EvaluacionDetalleDto>(almacen, cancelar)
            ?? Resultado<EvaluacionDetalleDto>.Ok(CambioEnServidor.Detalle(evaluacion, activo, numero));
    }

    private static Resultado<EvaluacionDetalleDto> Falla(TipoFallo fallo, string mensaje) =>
        Resultado<EvaluacionDetalleDto>.Falla(fallo, mensaje);
}

public sealed record SolicitudAsociacion(Guid IdEvaluacion, Guid? DesastreId, Actor Actor);

/// <summary>
/// Vincula una evaluación preliminar a un desastre (RF-ED-06, AD-29): deja de ser preliminar, conserva la versión
/// anterior y publica EV-26, para que aparezca en el mapa de ese desastre (E-06).
/// </summary>
public sealed class AsociarEvaluacion(IAlmacenEvaluaciones almacen, IPublicadorHechos hechos, ICodificador codificador,
    IReloj reloj) : ICasoDeUso<SolicitudAsociacion, EvaluacionDetalleDto>
{
    public async Task<Resultado<EvaluacionDetalleDto>> EjecutarAsync(SolicitudAsociacion solicitud, CancellationToken cancelar = default)
    {
        if (solicitud.DesastreId is not Guid idDesastre || idDesastre == Guid.Empty)
        {
            return Falla(TipoFallo.Invalido, "Falta desastreId");
        }
        var evaluacion = await almacen.EvaluacionAsync(solicitud.IdEvaluacion, cancelar);
        if (evaluacion is null)
        {
            return Falla(TipoFallo.NoEncontrado, $"No existe la evaluación {solicitud.IdEvaluacion}");
        }
        if (evaluacion.Estado != EstadoEvaluacion.PRELIMINAR || evaluacion.DesastreId is not null)
        {
            return Falla(TipoFallo.ReglaIncumplida,
                $"Solo se asocia una evaluación preliminar sin desastre; esta está {evaluacion.Estado}");
        }
        var desastre = await almacen.DesastreAsync(idDesastre, cancelar);
        if (desastre is null || !desastre.Registrado)
        {
            return Falla(TipoFallo.NoEncontrado, $"No existe el desastre {idDesastre}");
        }
        if (desastre.Cerrado)
        {
            return Falla(TipoFallo.ReglaIncumplida, $"El desastre {idDesastre} está cerrado");
        }

        var activo = await almacen.ActivoAsync(evaluacion.ActivoId, cancelar)
            ?? throw new InvalidOperationException($"La evaluación {evaluacion.Id} no tiene su activo {evaluacion.ActivoId}");
        var ahora = reloj.Ahora();
        string usuario = solicitud.Actor.Usuario;

        evaluacion.DesastreId = desastre.Id;
        evaluacion.Estado = EstadoEvaluacion.REGISTRADA;
        evaluacion.InstanteServidor = ahora;

        int numero = await CambioEnServidor.NuevaVersionAsync(almacen, codificador, evaluacion, MotivoVersion.ASOCIACION,
            usuario, $"Asociada al desastre {desastre.Id}", ahora, cancelar);
        var vigente = await almacen.EvaluacionVigenteDelActivoAsync(activo.Id, cancelar);
        CambioEnServidor.SubirVersionDelActivo(almacen, activo, ActivoVersion.AutorServidor(usuario),
            vigente == evaluacion.Id ? evaluacion.Id : null, ahora);
        hechos.Registrar(new EvaluacionActualizada(InstantaneaEvaluacion.De(evaluacion, activo, numero),
            MotivoActualizacion.ASOCIACION, usuario, ahora));

        return await CambioEnServidor.GuardarAsync<EvaluacionDetalleDto>(almacen, cancelar)
            ?? Resultado<EvaluacionDetalleDto>.Ok(CambioEnServidor.Detalle(evaluacion, activo, numero));
    }

    private static Resultado<EvaluacionDetalleDto> Falla(TipoFallo fallo, string mensaje) =>
        Resultado<EvaluacionDetalleDto>.Falla(fallo, mensaje);
}
