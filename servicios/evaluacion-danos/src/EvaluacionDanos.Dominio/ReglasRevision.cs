namespace EvaluacionDanos.Dominio;

/// <summary>Por qué no se puede hacer una operación de revisión.</summary>
public enum TipoRechazo
{
    /// <summary>El dato enviado no es válido (400).</summary>
    Invalido,
    /// <summary>El usuario tiene el rol, pero la regla de dominio no lo autoriza (403, AD-22 nivel 2).</summary>
    NoAutorizado,
    /// <summary>La regla de negocio no lo permite en el estado actual (422).</summary>
    ReglaIncumplida
}

public sealed record Rechazo(TipoRechazo Tipo, string Mensaje);

/// <summary>
/// Reglas de la revisión humana: validación (RF-ED-08, DA-09) y resolución de conflictos y duplicados
/// (AD-32, AD-33, E-14). Funciones puras: no tocan la base ni usan relojes.
/// </summary>
public static class ReglasRevision
{
    /// <summary>
    /// Valida un usuario de una organización que participa en el desastre (regla de nivel 2, EV-27). No se valida
    /// una evaluación con conflicto pendiente, una preliminar sin desastre ni una marcada como duplicado.
    /// </summary>
    public static Rechazo? Validacion(Evaluacion evaluacion, Desastre? desastre, bool conflictoPendiente,
        string? organizacion, EstadoValidacion nuevo, string? motivo)
    {
        if (evaluacion.Estado == EstadoEvaluacion.DUPLICADO)
        {
            return Regla($"La evaluación {evaluacion.Id} se fusionó como duplicado de {evaluacion.DuplicadoDe}; valide la vigente");
        }
        if (conflictoPendiente || evaluacion.Estado == EstadoEvaluacion.EN_CONFLICTO)
        {
            return Regla("La evaluación tiene un conflicto pendiente de revisión; resuélvalo antes de validarla");
        }
        if (evaluacion.DesastreId is null)
        {
            return Regla("La evaluación es preliminar: asóciela a un desastre antes de validarla");
        }
        if (desastre is null || !desastre.Registrado)
        {
            return Regla($"Aún no se conoce el desastre {evaluacion.DesastreId}");
        }
        if (!desastre.Participa(organizacion))
        {
            return new Rechazo(TipoRechazo.NoAutorizado,
                $"La organización '{organizacion}' no participa en el desastre {desastre.Id}");
        }
        if (nuevo == EstadoValidacion.RECHAZADA && string.IsNullOrWhiteSpace(motivo))
        {
            return new Rechazo(TipoRechazo.Invalido, "Para rechazar una evaluación hay que indicar el motivo");
        }
        if (motivo is { Length: > Limites.Descripcion })
        {
            return new Rechazo(TipoRechazo.Invalido, $"El motivo supera los {Limites.Descripcion} caracteres");
        }
        if (EstadoTras(nuevo) == evaluacion.Estado)
        {
            return Regla($"La evaluación ya está en el estado {evaluacion.Estado}");
        }
        return null;
    }

    /// <summary>Estado de la evaluación después de la validación. PENDIENTE la devuelve a registrada.</summary>
    public static EstadoEvaluacion EstadoTras(EstadoValidacion validacion) => validacion switch
    {
        EstadoValidacion.VALIDADA => EstadoEvaluacion.VALIDADA,
        EstadoValidacion.RECHAZADA => EstadoEvaluacion.RECHAZADA,
        _ => EstadoEvaluacion.REGISTRADA
    };

    /// <summary>Si la evaluación tenía una validación que deja de valer cuando su contenido cambia.</summary>
    public static bool TeniaValidacion(EstadoEvaluacion? estado) =>
        estado is EstadoEvaluacion.VALIDADA or EstadoEvaluacion.RECHAZADA;

    /// <summary>
    /// Qué decisiones admite cada tipo de conflicto. Un conflicto de versión se resuelve eligiendo cuál prevalece;
    /// un posible duplicado, fusionando o rechazando el duplicado. Sin evaluación vigente solo puede prevalecer
    /// la entrante.
    /// </summary>
    public static Rechazo? Resolucion(Conflicto conflicto, DecisionConflicto decision, bool hayVigente, string? justificacion)
    {
        if (conflicto.Estado != EstadoConflicto.PENDIENTE)
        {
            return Regla($"El conflicto {conflicto.Id} ya fue resuelto ({conflicto.Decision}) por {conflicto.DecididoPor}");
        }
        if (string.IsNullOrWhiteSpace(justificacion))
        {
            return new Rechazo(TipoRechazo.Invalido, "La decisión necesita una justificación");
        }
        if (justificacion.Length > Limites.Descripcion)
        {
            return new Rechazo(TipoRechazo.Invalido, $"La justificación supera los {Limites.Descripcion} caracteres");
        }
        bool eligeVersion = decision is DecisionConflicto.PREVALECE_ENTRANTE or DecisionConflicto.PREVALECE_VIGENTE;
        if (conflicto.Tipo == TipoConflicto.VERSION && !eligeVersion)
        {
            return Regla("Un conflicto de versión se resuelve con PREVALECE_ENTRANTE o PREVALECE_VIGENTE");
        }
        if (conflicto.Tipo == TipoConflicto.DUPLICADO && eligeVersion)
        {
            return Regla("Un posible duplicado se resuelve con FUSIONAR o RECHAZAR_DUPLICADO");
        }
        bool necesitaVigente = decision is DecisionConflicto.PREVALECE_VIGENTE or DecisionConflicto.FUSIONAR;
        if (necesitaVigente && !hayVigente)
        {
            return Regla("No hay una evaluación vigente con la cual resolver: solo puede prevalecer la entrante");
        }
        return null;
    }

    private static Rechazo Regla(string mensaje) => new(TipoRechazo.ReglaIncumplida, mensaje);
}
