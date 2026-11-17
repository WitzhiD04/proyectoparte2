using System.Globalization;

namespace Reconstruccion.Dominio;

/// <summary>Reglas para crear proyectos por lote a partir de evaluaciones.</summary>
public static class CreacionDeProyectos
{
    /// <summary>Evaluaciones que pueden originar un proyecto: validadas, con daño grave o
    /// destrucción total, y sin un proyecto vigente.</summary>
    public static List<EvaluacionValidada> Candidatas(IEnumerable<EvaluacionValidada> evaluaciones, IReadOnlySet<Guid> yaVinculadas)
    {
        List<EvaluacionValidada> candidatas = new();
        foreach (EvaluacionValidada evaluacion in evaluaciones)
        {
            if (ErrorDe(evaluacion, evaluacion.IdDesastre, yaVinculadas) is null)
            {
                candidatas.Add(evaluacion);
            }
        }
        return candidatas;
    }

    /// <summary>
    /// Candidatas que la autoridad puede ver: las de municipios de su jurisdicción (Cuadro 3 del SRS, DA-14).
    /// La nacional ve todas.
    /// </summary>
    public static List<EvaluacionValidada> Candidatas(IEnumerable<EvaluacionValidada> evaluaciones, IReadOnlySet<Guid> yaVinculadas,
        Actor actor)
    {
        List<EvaluacionValidada> candidatas = new();
        foreach (EvaluacionValidada evaluacion in Candidatas(evaluaciones, yaVinculadas))
        {
            if (Jurisdiccion.CubreMunicipio(actor, evaluacion.CodigoMunicipio))
            {
                candidatas.Add(evaluacion);
            }
        }
        return candidatas;
    }

    /// <summary>Valida todo el lote. Devuelve un error por cada evaluación que incumple una regla.</summary>
    public static List<string> ValidarLote(
        DesastreLocal? desastre,
        IReadOnlyList<Guid> idsSolicitados,
        IReadOnlyDictionary<Guid, EvaluacionValidada> evaluacionesConocidas,
        IReadOnlySet<Guid> yaVinculadas)
    {
        List<string> errores = new();
        if (desastre is null)
        {
            errores.Add("El desastre no existe.");
            return errores;
        }
        if (desastre.Estado != EstadosDesastre.EnReconstruccion)
        {
            errores.Add("Solo se crean proyectos para un desastre que está en reconstrucción.");
            return errores;
        }
        if (idsSolicitados.Count == 0)
        {
            errores.Add("El lote no tiene evaluaciones.");
            return errores;
        }
        if (idsSolicitados.Distinct().Count() != idsSolicitados.Count)
        {
            errores.Add("El lote repite evaluaciones.");
        }
        foreach (Guid id in idsSolicitados.Distinct())
        {
            if (!evaluacionesConocidas.TryGetValue(id, out EvaluacionValidada? evaluacion))
            {
                errores.Add($"Evaluación {id}: no existe o no está validada.");
                continue;
            }
            string? error = ErrorDe(evaluacion, desastre.Id, yaVinculadas);
            if (error is not null)
            {
                errores.Add($"Evaluación {id}: {error}");
            }
        }
        return errores;
    }

    /// <summary>Un proyecto por activo evaluado, con todas las evaluaciones de ese activo.</summary>
    public static List<ProyectoReconstruccion> CrearProyectos(
        Guid idDesastre, IReadOnlyList<EvaluacionValidada> evaluaciones, Actor actor, DateTimeOffset instante)
    {
        // Todo el lote o nada: si una evaluación está fuera de la jurisdicción de la autoridad, no se crea ninguno.
        Jurisdiccion.ExigirJurisdiccionSobre(actor, evaluaciones.Select(e => e.CodigoMunicipio));
        List<ProyectoReconstruccion> proyectos = new();
        foreach (IGrouping<Guid, EvaluacionValidada> porActivo in evaluaciones.GroupBy(e => e.IdActivo))
        {
            List<EvaluacionValidada> delActivo = porActivo.ToList();
            List<EvaluacionOrigen> origen = new();
            foreach (EvaluacionValidada evaluacion in delActivo)
            {
                origen.Add(new EvaluacionOrigen(evaluacion.Id, evaluacion.IdActivo, evaluacion.TipoActivo, evaluacion.NivelDano,
                    evaluacion.CodigoMunicipio));
            }
            // Un municipio: municipal; varios del mismo departamento: regional; varios departamentos: nacional (DA-14).
            // Se compara el código DIVIPOLA y no el nombre, que puede escribirse de varias formas.
            AlcanceTerritorial alcance = Jurisdiccion.AlcanceDe(delActivo.Select(e => e.CodigoMunicipio));
            string nombre = "Reconstrucción de " + delActivo[0].NombreActivo;
            proyectos.Add(ProyectoReconstruccion.Crear(Guid.NewGuid(), idDesastre, nombre, alcance, origen, actor, instante));
        }
        return proyectos;
    }

    private static string? ErrorDe(EvaluacionValidada evaluacion, Guid idDesastre, IReadOnlySet<Guid> yaVinculadas)
    {
        if (evaluacion.IdDesastre != idDesastre) return "pertenece a otro desastre.";
        if (evaluacion.Estado != EstadosEvaluacion.Validada) return "no está validada.";
        if (evaluacion.NivelDano != NivelDano.Grave && evaluacion.NivelDano != NivelDano.DestruccionTotal)
            return "su nivel de daño no es grave ni destrucción total.";
        if (yaVinculadas.Contains(evaluacion.Id)) return "ya está vinculada a un proyecto que no está cancelado.";
        return null;
    }
}

/// <summary>Cómo estaba un proyecto en una fecha, obtenido solo de su historial.</summary>
public sealed record EstadoEnFecha(
    string? Estado, decimal? Presupuesto, string? FuenteFinanciacion, string? Responsable, string? Contratista,
    string? Cronograma, decimal Avance, decimal CostoEjecutado, int Version);

public static class ReconstruccionHistorica
{
    /// <summary>Aplica en orden las entradas (ya filtradas hasta la fecha pedida).</summary>
    public static EstadoEnFecha? Reconstruir(IEnumerable<EntradaHistorial> entradas)
    {
        string? estado = null, fuente = null, responsable = null, contratista = null, cronograma = null;
        decimal? presupuesto = null;
        decimal avance = 0, costo = 0;
        int version = 0;
        foreach (EntradaHistorial entrada in entradas.OrderBy(e => e.Version))
        {
            version = entrada.Version;
            switch (entrada.TipoCambio)
            {
                case TipoCambio.Creacion:
                case TipoCambio.Estado: estado = entrada.ValorNuevo; break;
                case TipoCambio.Presupuesto: presupuesto = Numero(entrada.ValorNuevo); break;
                case TipoCambio.Fuente: fuente = entrada.ValorNuevo; break;
                case TipoCambio.Responsable: responsable = entrada.ValorNuevo; break;
                case TipoCambio.Contratista: contratista = entrada.ValorNuevo; break;
                case TipoCambio.Cronograma: cronograma = entrada.ValorNuevo; break;
                case TipoCambio.Avance: avance = Numero(entrada.ValorNuevo) ?? 0; break;
                case TipoCambio.Costo: costo = Numero(entrada.ValorNuevo) ?? 0; break;
            }
        }
        if (version == 0)
        {
            return null; // el proyecto no existía en esa fecha
        }
        return new EstadoEnFecha(estado, presupuesto, fuente, responsable, contratista, cronograma, avance, costo, version);
    }

    private static decimal? Numero(string? texto)
    {
        return texto is null ? null : decimal.Parse(texto, CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Fila del modelo de lectura: lo mínimo de un proyecto para armar el consolidado.
/// ActualizadoEn es el instante del evento que la dejó así: la consulta lo muestra como marca de tiempo del dato (AD-16).
/// CodigosMunicipio son los municipios del proyecto, para filtrar por jurisdicción (DA-14). Solo los trae EV-18; en
/// EV-19 y EV-20 llegan nulos y la proyección conserva los que ya tenía.
/// </summary>
public sealed record FilaPrograma(Guid IdProyecto, Guid IdDesastre, string Nombre, EstadoProyecto Estado,
    decimal Presupuesto, decimal CostoEjecutado, decimal Avance, int Version, DateTimeOffset ActualizadoEn,
    IReadOnlyList<string>? CodigosMunicipio = null);

public sealed record Consolidado(
    Guid IdDesastre,
    int TotalProyectos,
    IReadOnlyDictionary<EstadoProyecto, int> ProyectosPorEstado,
    decimal PresupuestoAsignado,
    decimal CostoEjecutado,
    decimal AvanceAgregado,
    IReadOnlyList<Guid> ProyectosEnSobrecosto,
    bool TodosEnEstadoFinal);

public static class ConsolidadoPrograma
{
    public static Consolidado Calcular(Guid idDesastre, IReadOnlyList<FilaPrograma> filas)
    {
        Dictionary<EstadoProyecto, int> porEstado = new();
        foreach (EstadoProyecto estado in Enum.GetValues<EstadoProyecto>())
        {
            porEstado[estado] = 0;
        }
        decimal presupuesto = 0, costo = 0, pesoAvance = 0, pesoTotal = 0;
        List<Guid> sobrecosto = new();
        bool todosFinales = true;
        foreach (FilaPrograma fila in filas)
        {
            porEstado[fila.Estado]++;
            presupuesto += fila.Presupuesto;
            costo += fila.CostoEjecutado;
            if (fila.Presupuesto > 0 && fila.CostoEjecutado > fila.Presupuesto) sobrecosto.Add(fila.IdProyecto);
            if (!MaquinaEstadosProyecto.EsFinal(fila.Estado)) todosFinales = false;
            // Avance agregado: promedio ponderado por presupuesto, sin contar los cancelados.
            if (fila.Estado != EstadoProyecto.Cancelado)
            {
                pesoAvance += fila.Avance * fila.Presupuesto;
                pesoTotal += fila.Presupuesto;
            }
        }
        decimal avance = pesoTotal == 0 ? 0 : Math.Round(pesoAvance / pesoTotal, 2);
        return new Consolidado(idDesastre, filas.Count, porEstado, presupuesto, costo, avance, sobrecosto, todosFinales);
    }
}
