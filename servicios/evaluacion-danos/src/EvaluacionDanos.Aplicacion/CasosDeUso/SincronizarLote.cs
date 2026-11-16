using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

/// <summary>
/// Aplica un lote de operaciones hechas sin conexión (FP-06, QA-14). Invariantes:
/// (1) ninguna rama borra ni sobrescribe: la evaluación vigente nunca se reemplaza por una entrante en conflicto;
/// (2) detecta conflictos y duplicados pero nunca los resuelve: conserva ambas versiones;
/// (3) reenviar una operación devuelve exactamente el mismo resultado y no crea nada;
/// (4) cada operación corre en su propia transacción: el error de una no tumba las demás.
/// Es la lógica del prototipo que verificó QA-14 (ServicioSincronizacion), con el almacén detrás de un puerto (DA-05).
/// Lo que se agregó no cambia ningún resultado: el municipio del activo nuevo (DA-07) y los hechos que se publican
/// por la bandeja en la misma transacción (AD-45).
/// </summary>
public sealed class SincronizarLote(
    IAlmacenSincronizacion almacen,
    IPublicadorHechos hechos,
    ICodificador codificador,
    Parametros parametros,
    IReloj reloj,
    IBitacoraSincronizacion bitacora)
{
    private sealed record Catalogo(IReadOnlySet<string> Tipos, IReadOnlySet<Guid> Desastres);

    private sealed record Contexto(string IdDispositivo, long DesfaseRelojMs, Catalogo Catalogo);

    public async Task<List<ResultadoOperacionDto>> ProcesarLoteAsync(
        LoteSincronizacion lote, DateTime instanteRecepcion, CancellationToken cancelar)
    {
        // EV-09 exige el municipio del activo. Sin la capa de municipios no se puede asignar, así que no se procesa
        // nada: la app conserva el lote en su cola y lo reenvía (nada se pierde ni queda INVALIDA por esto).
        if (!await almacen.CapaDeMunicipiosCargadaAsync(cancelar))
        {
            throw new CapaNoDisponibleException();
        }

        // Una vez por lote: cuánto va adelantado (+) o atrasado (−) el reloj del celular.
        long desfase = lote.InstanteEnvioDispositivo is null
            ? 0
            : (long)(instanteRecepcion - lote.InstanteEnvioDispositivo.Value.UtcDateTime).TotalMilliseconds;

        var (tipos, desastres) = await almacen.CatalogoAsync(cancelar);
        var contexto = new Contexto(lote.IdDispositivo!, desfase, new Catalogo(tipos, desastres));

        var resultados = new List<ResultadoOperacionDto>();
        foreach (var op in lote.Operaciones!)
        {
            try
            {
                resultados.Add(await ProcesarOperacionAsync(op, contexto, cancelar));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Solo llega aquí un error que tampoco se resolvió al reintentar. Se corta el lote: las
                // operaciones anteriores ya hicieron commit (cada una en su transacción) y las siguientes
                // pueden depender de esta (CREAR → ACTUALIZAR), así que no se procesan. Con el 503 la app
                // reenvía el lote completo, lo ya procesado devuelve su resultado guardado y se retoma
                // en orden desde aquí: nada se pierde ni se marca INVALIDA por error.
                bitacora.OperacionFallida(op.IdOperacion, ex);
                throw new LoteIncompletoException(resultados.Count, lote.Operaciones!.Count);
            }
        }
        return resultados;
    }

    /// <summary>
    /// Si al guardar se choca con otro lote que cambió el mismo activo en paralelo (token de concurrencia de
    /// Activo.Version o llave de ActivoVersion/OperacionProcesada), se revierte, se limpia el seguimiento y se
    /// reevalúa la operación UNA vez: en el segundo intento la regla ya ve la versión del otro dispositivo,
    /// o el paso 1 encuentra la operación ya procesada.
    /// </summary>
    private async Task<ResultadoOperacionDto> ProcesarOperacionAsync(Operacion op, Contexto ctx, CancellationToken cancelar)
    {
        for (int intento = 1; ; intento++)
        {
            almacen.Limpiar();
            try
            {
                return await IntentarAsync(op, ctx, cancelar);
            }
            catch (Exception ex) when (intento == 1 && almacen.EsCarrera(ex))
            {
                bitacora.ChoqueDeConcurrencia(op.IdOperacion, ex);
            }
            finally
            {
                almacen.Limpiar();
            }
        }
    }

    private async Task<ResultadoOperacionDto> IntentarAsync(Operacion op, Contexto ctx, CancellationToken cancelar)
    {
        // Sin idOperacion no hay cómo hacer el reenvío idempotente: se rechaza sin guardar nada.
        if (op.IdOperacion is not Guid idOperacion || idOperacion == Guid.Empty)
        {
            return Resultado(Guid.Empty, ResultadoOperacion.INVALIDA, EstadoEvaluacion.RECHAZADA,
                "Falta el atributo obligatorio 'idOperacion'", null);
        }

        await using var transaccion = await almacen.IniciarOperacionAsync(cancelar);

        // 1. REENVÍO: lo guardado la primera vez, sin tocar nada más.
        var guardado = await almacen.ResultadoGuardadoAsync(idOperacion, cancelar);
        if (guardado != null)
        {
            return codificador.Deserializar<ResultadoOperacionDto>(guardado);
        }

        // 2. INVÁLIDA: lo que se decide sin mirar evaluaciones ni activos.
        string? motivo = ValidadorEvaluacion.Validar(op, ctx.Catalogo.Tipos, ctx.Catalogo.Desastres);
        if (motivo != null)
        {
            return await GuardarAsync(transaccion, idOperacion, ctx, Invalida(idOperacion, motivo), cancelar);
        }

        var e = op.Evaluacion!;
        var tipo = Enum.Parse<TipoOperacion>(op.Tipo!);
        var evaluacionId = e.Id!.Value;
        var activoId = e.ActivoId!.Value;

        // 2b. INVÁLIDA: lo que depende de lo guardado.
        var existente = await almacen.EvaluacionAsync(evaluacionId, cancelar);
        motivo = tipo switch
        {
            TipoOperacion.CREAR when existente != null =>
                $"Ya existe una evaluación con id {evaluacionId}",
            TipoOperacion.ACTUALIZAR when existente == null =>
                $"No existe la evaluación {evaluacionId} que se quiere actualizar",
            TipoOperacion.ACTUALIZAR when existente!.IdDispositivo != ctx.IdDispositivo =>
                $"La evaluación {evaluacionId} pertenece a otro dispositivo",
            TipoOperacion.ACTUALIZAR when existente!.ActivoId != activoId =>
                "Una actualización no puede cambiar el activo de la evaluación",
            _ => null
        };
        motivo ??= await ValidarEvidenciasAsync(e, evaluacionId, cancelar);

        var activo = await almacen.ActivoAsync(activoId, cancelar);
        if (motivo == null && activo == null && e.ActivoNuevo == null)
        {
            motivo = $"El activo {activoId} no existe y la operación no trae activoNuevo";
        }
        // Una base que el servidor nunca emitió saltaría la regla de conflictos (sobrescritura silenciosa).
        if (motivo == null && activo != null && op.VersionBaseActivo is int b && (b < 0 || b > activo.Version))
        {
            motivo = $"versionBaseActivo {b} no es válida: el activo {activoId} va en la versión {activo.Version}";
        }
        if (motivo != null)
        {
            return await GuardarAsync(transaccion, idOperacion, ctx, Invalida(idOperacion, motivo), cancelar);
        }

        var resultado = activo == null
            ? await ActivoNuevoAsync(idOperacion, e, ctx, cancelar)
            : await ActivoExistenteAsync(idOperacion, tipo, op.VersionBaseActivo, e, existente, activo, ctx, cancelar);

        return await GuardarAsync(transaccion, idOperacion, ctx, resultado, cancelar);
    }

    // ---------- 3. Activo nuevo ----------

    private async Task<ResultadoOperacionDto> ActivoNuevoAsync(
        Guid idOperacion, EvaluacionEntrante e, Contexto ctx, CancellationToken cancelar)
    {
        var ahora = reloj.Ahora();
        var nuevo = e.ActivoNuevo!;
        var instanteCampo = e.InstanteCampo!.Value.UtcDateTime;

        // El servidor asigna el municipio (DA-07, E-12): no se le pide al evaluador. La capa estaba cargada al
        // empezar el lote; si aquí no hay municipio el error corta el lote y la app lo reenvía.
        var municipio = await almacen.MunicipioDeAsync(nuevo.Lat!.Value, nuevo.Lon!.Value, cancelar)
            ?? throw new CapaNoDisponibleException();

        var activo = new Activo
        {
            Id = e.ActivoId!.Value,
            TipoActivoCodigo = nuevo.TipoCodigo!,
            Lat = nuevo.Lat!.Value,
            Lon = nuevo.Lon!.Value,
            Referencia = nuevo.Referencia ?? "",
            Version = 1,
            CreadoPorDispositivo = ctx.IdDispositivo,
            CodigoMunicipio = municipio.Codigo,
            VersionCapa = municipio.VersionCapa,
            ActualizadoEn = ahora
        };
        almacen.Agregar(activo);

        await almacen.BloquearTipoAsync(activo.TipoActivoCodigo, cancelar);
        var duplicado = await BuscarDuplicadoAsync(activo, instanteCampo, cancelar);
        bool enConflicto = duplicado != null;

        var evaluacion = NuevaEvaluacion(e, ctx, ahora, enConflicto ? EstadoEvaluacion.EN_CONFLICTO : EstadoInicial(e));
        almacen.Agregar(evaluacion);
        var version = NuevaVersion(e, evaluacion.Id, 1, null, enConflicto, ctx, ahora);
        almacen.Agregar(version);
        almacen.Agregar(new ActivoVersion
        {
            ActivoId = activo.Id, Numero = 1, IdDispositivo = ctx.IdDispositivo, EvaluacionId = evaluacion.Id, Instante = ahora
        });
        AgregarEvidencias(e, evaluacion.Id, yaDeclaradas: new HashSet<Guid>());
        RegistrarSincronizada(e, activo, evaluacion.Estado, 1, ctx, ahora, enConflicto, versionesConservadas: enConflicto ? 2 : 1);

        if (duplicado is null)
        {
            return Resultado(idOperacion, ResultadoOperacion.APLICADA, evaluacion.Estado, null, 1, ahora);
        }

        double distancia = Haversine.DistanciaMetros(activo.Lat, activo.Lon, duplicado.Lat, duplicado.Lon);
        double horas = Math.Abs((duplicado.InstanteCampo - instanteCampo).TotalHours);
        string regla = $"Posible duplicado: el activo {duplicado.ActivoId} del mismo tipo ({activo.TipoActivoCodigo}) " +
                       $"está a {distancia:0.0} m y tiene la evaluación {duplicado.EvaluacionId} a {horas:0.0} h " +
                       $"(umbral {parametros.DistanciaDuplicadoMetros} m, ±{parametros.VentanaDuplicadoHoras} h). Dispositivo: {ctx.IdDispositivo}";
        await CrearConflictoAsync(activo.Id, TipoConflicto.DUPLICADO, duplicado.EvaluacionId, evaluacion.Id, version, null,
            regla, ahora, cancelar);

        return Resultado(idOperacion, ResultadoOperacion.POSIBLE_DUPLICADO, EstadoEvaluacion.EN_CONFLICTO,
            "Hay otro activo del mismo tipo muy cerca con una evaluación reciente; queda para revisión", 1, ahora);
    }

    private async Task<CandidatoDuplicado?> BuscarDuplicadoAsync(Activo activo, DateTime instanteCampo, CancellationToken cancelar)
    {
        double distanciaMetros = parametros.DistanciaDuplicadoMetros;
        double ventanaHoras = parametros.VentanaDuplicadoHoras;
        // Recuadro en la consulta para no traer toda la tabla; la distancia exacta la decide Haversine en memoria.
        double dLat = Haversine.GradosLatitud(distanciaMetros);
        double dLon = Haversine.GradosLongitud(distanciaMetros, activo.Lat);
        var desde = instanteCampo.AddHours(-ventanaHoras);
        var hasta = instanteCampo.AddHours(ventanaHoras);
        double minLat = activo.Lat - dLat, maxLat = activo.Lat + dLat;
        double minLon = activo.Lon - dLon, maxLon = activo.Lon + dLon;
        string tipo = activo.TipoActivoCodigo;

        var candidatos = await almacen.CandidatosDuplicadoAsync(tipo, activo.Id, minLat, maxLat, minLon, maxLon,
            desde, hasta, cancelar);

        return DetectorDuplicados.Buscar(activo.Lat, activo.Lon, tipo, instanteCampo, candidatos, distanciaMetros, ventanaHoras);
    }

    // ---------- 4. Activo existente ----------

    private async Task<ResultadoOperacionDto> ActivoExistenteAsync(
        Guid idOperacion, TipoOperacion tipo, int? versionBase, EvaluacionEntrante e,
        Evaluacion? existente, Activo activo, Contexto ctx, CancellationToken cancelar)
    {
        var ahora = reloj.Ahora();
        var versiones = await almacen.VersionesDelActivoAsync(activo.Id, cancelar);
        var veredicto = ReglaConflicto.Evaluar(versionBase, ctx.IdDispositivo, versiones);

        int numeroVersion = 1;
        IReadOnlySet<Guid> yaDeclaradas = new HashSet<Guid>();
        if (existente != null)
        {
            numeroVersion = await almacen.UltimoNumeroVersionAsync(existente.Id, cancelar);
            numeroVersion++;
            yaDeclaradas = await almacen.EvidenciasDeclaradasAsync(existente.Id, cancelar);
        }

        if (!veredicto.HayConflicto)
        {
            // Si la evaluación tiene un conflicto pendiente, la edición se aplica pero el estado sigue
            // EN_CONFLICTO: el servidor no da por resuelto nada, eso es de la revisión humana.
            bool conflictoPendiente = existente != null
                && await almacen.TieneConflictoPendienteComoEntranteAsync(existente.Id, null, cancelar);
            var estado = conflictoPendiente ? EstadoEvaluacion.EN_CONFLICTO : EstadoInicial(e);
            Evaluacion evaluacion;
            ValidacionCambiada? validacionRetirada = null;
            if (tipo == TipoOperacion.CREAR)
            {
                evaluacion = NuevaEvaluacion(e, ctx, ahora, estado);
                almacen.Agregar(evaluacion);
            }
            else
            {
                evaluacion = existente!;
                var estadoPrevio = evaluacion.Estado;
                var desastrePrevio = evaluacion.DesastreId;
                CopiarContenido(e, evaluacion, ctx, ahora);
                evaluacion.Estado = estado;
                // El contenido validado cambió: la validación deja de valer y se avisa con EV-08 (E-02), para que
                // quien la usaba (las candidatas de Reconstrucción) no se quede con una evaluación que ya no es esa.
                if (ReglasRevision.TeniaValidacion(estadoPrevio) && desastrePrevio is Guid idDesastre)
                {
                    evaluacion.ValidadoPor = evaluacion.Evaluador;
                    evaluacion.InstanteValidacion = ahora;
                    validacionRetirada = new ValidacionCambiada(null!, idDesastre, evaluacion.Evaluador,
                        evaluacion.Organizacion, activo.Referencia, EstadoValidacion.PENDIENTE, evaluacion.Evaluador, ahora);
                }
            }

            almacen.Agregar(NuevaVersion(e, evaluacion.Id, numeroVersion, versionBase, false, ctx, ahora));
            int nuevaVersion = activo.Version + 1;
            activo.Version = nuevaVersion;   // UPDATE ... WHERE version = <la leída>
            activo.ActualizadoEn = ahora;
            almacen.Agregar(new ActivoVersion
            {
                ActivoId = activo.Id, Numero = nuevaVersion, IdDispositivo = ctx.IdDispositivo, EvaluacionId = evaluacion.Id, Instante = ahora
            });
            AgregarEvidencias(e, evaluacion.Id, yaDeclaradas);
            var instantanea = RegistrarSincronizada(e, activo, estado, numeroVersion, ctx, ahora, conflictoPendiente, numeroVersion);
            if (validacionRetirada != null)
            {
                hechos.Registrar(validacionRetirada with { Evaluacion = instantanea });
            }
            return Resultado(idOperacion, ResultadoOperacion.APLICADA, estado, null, nuevaVersion, ahora);
        }

        // Con conflicto: NO se toca la evaluación vigente ni Activo.Version. Se guarda la entrante completa.
        var vigenteId = await almacen.EvaluacionVigenteDelActivoAsync(activo.Id, cancelar);

        Evaluacion entrante;
        EstadoEvaluacion? estadoPrevioEntrante = null;
        if (tipo == TipoOperacion.CREAR)
        {
            entrante = NuevaEvaluacion(e, ctx, ahora, EstadoEvaluacion.EN_CONFLICTO);
            almacen.Agregar(entrante);
        }
        else
        {
            // Solo se marca: el contenido nuevo queda en la EvaluacionVersion en conflicto.
            entrante = existente!;
            // Si ya estaba en conflicto, el estado por restituir es el del primer conflicto, no EN_CONFLICTO.
            estadoPrevioEntrante = entrante.Estado == EstadoEvaluacion.EN_CONFLICTO ? null : entrante.Estado;
            entrante.Estado = EstadoEvaluacion.EN_CONFLICTO;
        }

        var version = NuevaVersion(e, entrante.Id, numeroVersion, versionBase, true, ctx, ahora);
        almacen.Agregar(version);
        AgregarEvidencias(e, entrante.Id, yaDeclaradas);
        await CrearConflictoAsync(activo.Id, TipoConflicto.VERSION, vigenteId, entrante.Id, version, estadoPrevioEntrante,
            veredicto.Detalle, ahora, cancelar);
        RegistrarSincronizada(e, activo, EstadoEvaluacion.EN_CONFLICTO, numeroVersion, ctx, ahora, true,
            numeroVersion + (vigenteId is null ? 0 : 1));

        return Resultado(idOperacion, ResultadoOperacion.EN_CONFLICTO, EstadoEvaluacion.EN_CONFLICTO,
            "El activo cambió en el servidor desde la descarga; se conservan ambas versiones para revisión", null, ahora);
    }

    // ---------- Piezas comunes ----------

    private async Task<string?> ValidarEvidenciasAsync(EvaluacionEntrante e, Guid evaluacionId, CancellationToken cancelar)
    {
        if (e.Evidencias is null || e.Evidencias.Count == 0)
        {
            return null;
        }
        var ids = new List<Guid>();
        foreach (var ev in e.Evidencias)
        {
            ids.Add(ev.Id!.Value);
        }
        var deOtra = await almacen.EvidenciaDeOtraEvaluacionAsync(ids, evaluacionId, cancelar);
        return deOtra is null ? null : $"La evidencia {deOtra} ya pertenece a otra evaluación";
    }

    private void AgregarEvidencias(EvaluacionEntrante e, Guid evaluacionId, IReadOnlySet<Guid> yaDeclaradas)
    {
        if (e.Evidencias is null)
        {
            return;
        }
        foreach (var ev in e.Evidencias)
        {
            if (yaDeclaradas.Contains(ev.Id!.Value))
            {
                continue;
            }
            almacen.Agregar(new Evidencia
            {
                Id = ev.Id.Value,
                EvaluacionId = evaluacionId,
                Lat = ev.Lat!.Value,
                Lon = ev.Lon!.Value,
                InstanteCaptura = ev.InstanteCaptura!.Value.UtcDateTime,
                Sha256Declarado = ev.Sha256!.ToLowerInvariant(),
                TamanoBytes = ev.TamanoBytes!.Value,
                Subida = false
            });
        }
    }

    private static Evaluacion NuevaEvaluacion(EvaluacionEntrante e, Contexto ctx, DateTime ahora, EstadoEvaluacion estado)
    {
        var evaluacion = new Evaluacion { Id = e.Id!.Value, ActivoId = e.ActivoId!.Value, Estado = estado };
        CopiarContenido(e, evaluacion, ctx, ahora);
        return evaluacion;
    }

    private static void CopiarContenido(EvaluacionEntrante e, Evaluacion destino, Contexto ctx, DateTime ahora)
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
        destino.IdDispositivo = ctx.IdDispositivo;
        destino.DesfaseRelojMs = ctx.DesfaseRelojMs;
    }

    private EvaluacionVersion NuevaVersion(EvaluacionEntrante e, Guid evaluacionId, int numero, int? versionBase,
        bool enConflicto, Contexto ctx, DateTime ahora) => new()
    {
        Id = Guid.NewGuid(),
        EvaluacionId = evaluacionId,
        NumeroVersion = numero,
        ContenidoJson = codificador.Serializar(e),
        Autor = e.Evaluador!.Trim(),
        IdDispositivo = ctx.IdDispositivo,
        VersionActivoBase = versionBase,
        EnConflicto = enConflicto,
        Instante = ahora,
        Motivo = MotivoVersion.SINCRONIZACION
    };

    private async Task CrearConflictoAsync(Guid activoId, TipoConflicto tipo, Guid? vigenteId, Guid entranteId,
        EvaluacionVersion versionEntrante, EstadoEvaluacion? estadoPrevioEntrante, string regla, DateTime ahora,
        CancellationToken cancelar)
    {
        var conflicto = new Conflicto
        {
            Id = Guid.NewGuid(),
            ActivoId = activoId,
            Tipo = tipo,
            EvaluacionVigenteId = vigenteId,
            EvaluacionEntranteId = entranteId,
            VersionEntranteId = versionEntrante.Id,
            Regla = regla,
            Estado = EstadoConflicto.PENDIENTE,
            Instante = ahora,
            EstadoPrevioEntrante = estadoPrevioEntrante
        };
        almacen.Agregar(conflicto);

        // Las dos versiones que se conservan (AD-32): la vigente, si existe, y la entrante.
        var ids = new List<Guid>();
        var versiones = new List<VersionEnConflicto>();
        if (vigenteId is Guid v)
        {
            ids.Add(v);
            int numeroVigente = await almacen.NumeroVersionVigenteAsync(v, cancelar) ?? 1;
            versiones.Add(new VersionEnConflicto(v, numeroVigente, true));
        }
        ids.Add(entranteId);
        versiones.Add(new VersionEnConflicto(entranteId, versionEntrante.NumeroVersion, false));
        hechos.Registrar(new ConflictoDetectado(conflicto.Id, activoId, tipo, ids, regla, versiones, ahora));
    }

    /// <summary>versionesConservadas: versiones guardadas de la evaluación, más la vigente si quedó en conflicto.</summary>
    private InstantaneaEvaluacion RegistrarSincronizada(EvaluacionEntrante e, Activo activo, EstadoEvaluacion estado,
        int versionEvaluacion, Contexto ctx, DateTime ahora, bool enConflicto, int versionesConservadas)
    {
        // Los datos son los de la versión que llegó (la que se acaba de guardar), aunque haya quedado en conflicto.
        var instantanea = new InstantaneaEvaluacion(e.Id!.Value, activo.Id, e.DesastreId, e.TipoActivoCodigo!,
            Enum.Parse<NivelDano>(e.NivelDano!), e.Lat!.Value, e.Lon!.Value, e.PrecisionM!.Value, activo.CodigoMunicipio,
            estado, versionEvaluacion, activo.Version);
        hechos.Registrar(new EvaluacionSincronizada(instantanea, ctx.IdDispositivo, e.InstanteCampo!.Value.UtcDateTime,
            enConflicto, versionesConservadas, ahora));
        return instantanea;
    }

    /// <summary>7. Siempre se guarda el resultado, dentro de la misma transacción que los cambios.</summary>
    private async Task<ResultadoOperacionDto> GuardarAsync(ITransaccionOperacion transaccion,
        Guid idOperacion, Contexto ctx, ResultadoOperacionDto resultado, CancellationToken cancelar)
    {
        almacen.Agregar(new OperacionProcesada
        {
            IdOperacion = idOperacion,
            IdDispositivo = ctx.IdDispositivo,
            ResultadoJson = codificador.Serializar(resultado),
            Instante = resultado.InstanteServidor
        });
        await transaccion.GuardarYConfirmarAsync(cancelar);
        return resultado;
    }

    private static EstadoEvaluacion EstadoInicial(EvaluacionEntrante e) =>
        e.DesastreId is null ? EstadoEvaluacion.PRELIMINAR : EstadoEvaluacion.REGISTRADA;

    private ResultadoOperacionDto Invalida(Guid idOperacion, string motivo) =>
        Resultado(idOperacion, ResultadoOperacion.INVALIDA, EstadoEvaluacion.RECHAZADA, motivo, null);

    private ResultadoOperacionDto Resultado(Guid idOperacion, ResultadoOperacion resultado, EstadoEvaluacion? estado,
        string? motivo, int? nuevaVersion, DateTime? instante = null) =>
        new(idOperacion, resultado, estado, motivo, nuevaVersion, instante ?? reloj.Ahora());
}

public class LoteIncompletoException(int procesadas, int total)
    : Exception($"Se procesaron {procesadas} de {total} operaciones y la siguiente falló; reenvíe el lote " +
                "(lo ya procesado devolverá el mismo resultado)")
{
    public int Procesadas { get; } = procesadas;
}

/// <summary>Aún no llegó la capa de municipios (EV-21): no se puede asignar el municipio que exige EV-09.</summary>
public class CapaNoDisponibleException()
    : Exception("El servicio aún no tiene la capa de municipios; reenvíe el lote en unos segundos (nada se perdió)");
