using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

public sealed record SolicitudDescarga(Guid? DesastreId, string? Zona, DateTime? Desde);

/// <summary>
/// Descarga previa (RF-ED-12, AD-31): catálogo con criterios, desastres activos y activos de la zona con su versión.
/// Filtrada e incremental (DA-19, E-26):
/// - Desastres: solo los que no están cerrados; con <c>desastreId</c>, solo ese.
/// - Activos: con <c>zona</c> (DIVIPOLA de municipio o de departamento), los de esa zona; sin zona y con desastre,
///   los de la delimitación vigente del desastre (EV-05). Sin filtros, o si el desastre aún no tiene delimitación,
///   todos: es lo que la app actual pide y evita que un evaluador cree un duplicado por no tener el activo.
/// - Con <c>desde</c> (el <c>instanteServidor</c> de la descarga anterior), solo los activos que cambiaron después.
///   El catálogo y los desastres van siempre completos, porque son pocos.
/// </summary>
public sealed class ConsultarDescarga(IConsultasEvaluaciones consultas, Parametros parametros, IReloj reloj)
    : ICasoDeUso<SolicitudDescarga, DescargaDto>
{
    public async Task<Resultado<DescargaDto>> EjecutarAsync(SolicitudDescarga solicitud, CancellationToken cancelar = default)
    {
        string? zona = string.IsNullOrWhiteSpace(solicitud.Zona) ? null : solicitud.Zona.Trim();
        if (zona != null && !(zona.Length is 2 or 5 && zona.All(char.IsAsciiDigit)))
        {
            return Resultado<DescargaDto>.Falla(TipoFallo.Invalido,
                "zona debe ser un código DIVIPOLA: 5 dígitos (municipio) o 2 (departamento)");
        }

        // Se toma antes de leer: lo que se confirme mientras tanto entra en la siguiente descarga incremental.
        var instante = reloj.Ahora();
        var tipos = await consultas.TiposActivoAsync(cancelar);
        var desastres = await consultas.DesastresAsync(soloAbiertos: true, solicitud.DesastreId, cancelar);

        IReadOnlyCollection<string>? municipios = null;
        string? departamento = null;
        if (zona is { Length: 5 })
        {
            municipios = [zona];
        }
        else if (zona is { Length: 2 })
        {
            departamento = zona;
        }
        else if (solicitud.DesastreId is not null && desastres.Count == 1 && desastres[0].CodigosMunicipio is { Count: > 0 } delimitacion)
        {
            municipios = delimitacion;
        }

        // El traslape cubre los cambios confirmados después de que la descarga anterior leyó su reloj. Repetir un
        // activo no hace daño: la app lo reemplaza por su id.
        DateTime? desde = solicitud.Desde?.ToUniversalTime().AddSeconds(-parametros.MargenDescargaSegundos);
        var activos = await consultas.ActivosAsync(municipios, departamento, desde, cancelar);

        return Resultado<DescargaDto>.Ok(new DescargaDto(instante, tipos, desastres, activos,
            Incremental: solicitud.Desde is not null, Desde: solicitud.Desde?.ToUniversalTime()));
    }
}

/// <summary>Conflictos y posibles duplicados con ambas versiones completas, para la revisión humana (AD-32, E-13).</summary>
public sealed class ConsultarConflictos(IConsultasEvaluaciones consultas) : ICasoDeUso<string, List<ConflictoDto>>
{
    public async Task<Resultado<List<ConflictoDto>>> EjecutarAsync(string estado, CancellationToken cancelar = default)
    {
        if (!ValidadorEvaluacion.EsNombreDeEnum<EstadoConflicto>(estado))
        {
            return Resultado<List<ConflictoDto>>.Falla(TipoFallo.Invalido, $"Estado '{estado}' no válido (PENDIENTE o RESUELTO)");
        }
        return Resultado<List<ConflictoDto>>.Ok(await consultas.ConflictosAsync(Enum.Parse<EstadoConflicto>(estado), cancelar));
    }
}

/// <summary>Evaluaciones de un dispositivo con su estado y número de versiones (E-09): verifica QA-14.</summary>
public sealed class ConsultarEvaluacionesDeDispositivo(IConsultasEvaluaciones consultas)
    : ICasoDeUso<string?, List<EvaluacionResumenDto>>
{
    public async Task<Resultado<List<EvaluacionResumenDto>>> EjecutarAsync(string? idDispositivo, CancellationToken cancelar = default)
    {
        if (string.IsNullOrWhiteSpace(idDispositivo))
        {
            return Resultado<List<EvaluacionResumenDto>>.Falla(TipoFallo.Invalido, "Falta el parámetro idDispositivo");
        }
        return Resultado<List<EvaluacionResumenDto>>.Ok(await consultas.EvaluacionesDeDispositivoAsync(idDispositivo, cancelar));
    }
}

/// <summary>Lista de desastres de la copia local, para la página del mapa (obsoleta).</summary>
public sealed class ConsultarDesastres(IConsultasEvaluaciones consultas) : ICasoDeUso<bool, List<DesastreDto>>
{
    public async Task<Resultado<List<DesastreDto>>> EjecutarAsync(bool soloAbiertos, CancellationToken cancelar = default) =>
        Resultado<List<DesastreDto>>.Ok(await consultas.DesastresAsync(soloAbiertos, null, cancelar));
}

public sealed record SolicitudMapaCalor(Guid? DesastreId, string? NivelMinimo);

/// <summary>
/// Un punto por activo, en sus coordenadas, con el nivel de su evaluación VIGENTE (la de la última versión
/// aplicada del activo). Si el activo tiene un conflicto pendiente cuenta la vigente y no la entrante,
/// para no contarlo dos veces. Peso: sin daño 0, leve 0,25, moderado 0,5, grave 0,75, destrucción total 1.
/// OBSOLETO: se conserva igual que en el prototipo hasta que lo reemplace Consulta de Daños (DA-08, paso 5).
/// </summary>
public sealed class ConsultarMapaCalor(IConsultasEvaluaciones consultas) : ICasoDeUso<SolicitudMapaCalor, MapaCalorDto>
{
    public static double Peso(NivelDano nivel) => nivel switch
    {
        NivelDano.SIN_DANO => 0,
        NivelDano.LEVE => 0.25,
        NivelDano.MODERADO => 0.5,
        NivelDano.GRAVE => 0.75,
        NivelDano.DESTRUCCION_TOTAL => 1,
        _ => 0
    };

    public async Task<Resultado<MapaCalorDto>> EjecutarAsync(SolicitudMapaCalor solicitud, CancellationToken cancelar = default)
    {
        if (solicitud.DesastreId is not Guid desastreId)
        {
            return Resultado<MapaCalorDto>.Falla(TipoFallo.Invalido, "Falta el parámetro desastreId");
        }
        var nivelMinimo = NivelDano.SIN_DANO;
        if (!string.IsNullOrWhiteSpace(solicitud.NivelMinimo))
        {
            if (!ValidadorEvaluacion.EsNombreDeEnum<NivelDano>(solicitud.NivelMinimo))
            {
                return Resultado<MapaCalorDto>.Falla(TipoFallo.Invalido,
                    $"nivelMinimo '{solicitud.NivelMinimo}' no es un nivel de daño válido");
            }
            nivelMinimo = Enum.Parse<NivelDano>(solicitud.NivelMinimo);
        }

        var datos = await consultas.MapaCalorAsync(desastreId, cancelar);
        if (datos is null)
        {
            return Resultado<MapaCalorDto>.Falla(TipoFallo.NoEncontrado, $"No existe el desastre {desastreId}");
        }

        var conteo = new Dictionary<string, int>();
        foreach (var n in Enum.GetValues<NivelDano>())
        {
            conteo[n.ToString()] = 0;
        }

        var puntos = new List<PuntoCalorDto>();
        foreach (var x in datos.Vigentes)
        {
            if (x.NivelDano < nivelMinimo)
            {
                continue;
            }
            conteo[x.NivelDano.ToString()]++;
            puntos.Add(new PuntoCalorDto(x.ActivoId, x.TipoActivoCodigo, x.Lat, x.Lon, x.NivelDano, Peso(x.NivelDano),
                datos.ActivosEnConflicto.Contains(x.ActivoId)));
        }

        return Resultado<MapaCalorDto>.Ok(new MapaCalorDto(desastreId, nivelMinimo, datos.InstanteDato, DateTime.UtcNow,
            datos.ConflictosPendientes, conteo, puntos));
    }
}
