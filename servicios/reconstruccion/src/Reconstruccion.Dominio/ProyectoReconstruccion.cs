using System.Globalization;

namespace Reconstruccion.Dominio;

/// <summary>Datos planos de un proyecto, para guardarlo y volver a cargarlo sin exponer sus campos.</summary>
public sealed record InstantaneaProyecto(
    Guid Id,
    Guid IdDesastre,
    Guid IdActivo,
    string Nombre,
    AlcanceTerritorial Alcance,
    EstadoProyecto Estado,
    decimal? Presupuesto,
    string? FuenteFinanciacion,
    string? Responsable,
    string? Contratista,
    DateOnly? Inicio,
    DateOnly? Fin,
    decimal Avance,
    decimal CostoEjecutado,
    int Version,
    IReadOnlyList<EvaluacionOrigen> Evaluaciones);

/// <summary>
/// Proyecto de reconstrucción. Cada operación de negocio es un método que valida sus reglas,
/// cambia el estado y deja su entrada de historial. Si una regla no se cumple, lanza y no cambia nada.
/// </summary>
public sealed class ProyectoReconstruccion
{
    public const string ProcedenciaPlataforma = "plataforma";

    private readonly List<EvaluacionOrigen> _evaluaciones = new();
    private readonly List<EntradaHistorial> _entradasNuevas = new();
    private readonly List<RegistroAvance> _avancesNuevos = new();
    private readonly List<RegistroCosto> _costosNuevos = new();
    private readonly List<Documento> _documentosNuevos = new();

    public Guid Id { get; private set; }
    public Guid IdDesastre { get; private set; }
    public Guid IdActivo { get; private set; }
    public string Nombre { get; private set; } = "";
    public AlcanceTerritorial Alcance { get; private set; }
    public EstadoProyecto Estado { get; private set; }
    public Dinero? Presupuesto { get; private set; }
    public string? FuenteFinanciacion { get; private set; }
    public string? Responsable { get; private set; }
    public string? Contratista { get; private set; }
    public Cronograma? Cronograma { get; private set; }
    public decimal Avance { get; private set; }
    public Dinero CostoEjecutado { get; private set; } = Dinero.Cero;
    /// <summary>Número de cambios registrados. Coincide con el número de entradas de historial.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<EvaluacionOrigen> Evaluaciones => _evaluaciones;

    // Lo que la operación en curso produjo y todavía no se ha guardado.
    public IReadOnlyList<EntradaHistorial> EntradasNuevas => _entradasNuevas;
    public IReadOnlyList<RegistroAvance> AvancesNuevos => _avancesNuevos;
    public IReadOnlyList<RegistroCosto> CostosNuevos => _costosNuevos;
    public IReadOnlyList<Documento> DocumentosNuevos => _documentosNuevos;

    public bool EnSobrecosto => Presupuesto.HasValue && CostoEjecutado.Monto > Presupuesto.Value.Monto;

    /// <summary>Municipios (DIVIPOLA) de las evaluaciones que originaron el proyecto.</summary>
    public IReadOnlyList<string> CodigosMunicipio => _evaluaciones.Select(e => e.CodigoMunicipio).Distinct().OrderBy(c => c).ToList();

    private ProyectoReconstruccion() { }

    // ------------------------------------------------------------------ creación y carga

    /// <summary>Crea un proyecto en Formulado a partir de las evaluaciones de un mismo activo.</summary>
    public static ProyectoReconstruccion Crear(
        Guid id,
        Guid idDesastre,
        string nombre,
        AlcanceTerritorial alcance,
        IReadOnlyList<EvaluacionOrigen> evaluaciones,
        Actor actor,
        DateTimeOffset instante)
    {
        ExigirRol(actor, Rol.Autoridad, "Solo la autoridad crea proyectos.");
        if (evaluaciones.Count == 0)
        {
            throw new ReglaDeNegocioException("SIN_EVALUACIONES", "Un proyecto se crea a partir de una o más evaluaciones.");
        }

        ProyectoReconstruccion proyecto = new()
        {
            Id = id,
            IdDesastre = idDesastre,
            IdActivo = evaluaciones[0].IdActivo,
            Nombre = nombre,
            Alcance = alcance,
            Estado = EstadoProyecto.Formulado,
        };
        proyecto._evaluaciones.AddRange(evaluaciones);
        proyecto.Anotar(TipoCambio.Creacion, null, EstadoProyecto.Formulado.ToString(), actor.Usuario,
            "Creado a partir de evaluaciones de daño", instante);
        return proyecto;
    }

    public static ProyectoReconstruccion Rehidratar(InstantaneaProyecto datos)
    {
        ProyectoReconstruccion proyecto = new()
        {
            Id = datos.Id,
            IdDesastre = datos.IdDesastre,
            IdActivo = datos.IdActivo,
            Nombre = datos.Nombre,
            Alcance = datos.Alcance,
            Estado = datos.Estado,
            Presupuesto = datos.Presupuesto.HasValue ? new Dinero(datos.Presupuesto.Value) : null,
            FuenteFinanciacion = datos.FuenteFinanciacion,
            Responsable = datos.Responsable,
            Contratista = datos.Contratista,
            Cronograma = datos.Inicio.HasValue && datos.Fin.HasValue ? new Cronograma(datos.Inicio.Value, datos.Fin.Value) : null,
            Avance = datos.Avance,
            CostoEjecutado = new Dinero(datos.CostoEjecutado),
            Version = datos.Version,
        };
        proyecto._evaluaciones.AddRange(datos.Evaluaciones);
        return proyecto;
    }

    public InstantaneaProyecto AInstantanea()
    {
        return new InstantaneaProyecto(
            Id, IdDesastre, IdActivo, Nombre, Alcance, Estado,
            Presupuesto?.Monto, FuenteFinanciacion, Responsable, Contratista,
            Cronograma?.Inicio, Cronograma?.Fin, Avance, CostoEjecutado.Monto, Version,
            _evaluaciones.ToList());
    }

    public void LimpiarPendientes()
    {
        _entradasNuevas.Clear();
        _avancesNuevos.Clear();
        _costosNuevos.Clear();
        _documentosNuevos.Clear();
    }

    // ------------------------------------------------------------------ asignación

    /// <summary>Asigna o modifica presupuesto, fuente de financiación y cronograma. Solo cambia lo que llega.</summary>
    public void AsignarPresupuesto(Actor actor, Dinero? presupuesto, string? fuente, Cronograma? cronograma,
        string justificacion, DateTimeOffset instante)
    {
        ExigirRol(actor, Rol.Autoridad, "Solo la autoridad asigna presupuesto.");
        MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.Asignar);
        ExigirJustificacion(justificacion);
        if (presupuesto is null && fuente is null && cronograma is null)
        {
            throw new ReglaDeNegocioException("SIN_CAMBIOS", "No se indicó ningún valor para asignar.");
        }
        if (fuente is not null && string.IsNullOrWhiteSpace(fuente))
        {
            throw new ReglaDeNegocioException("FUENTE_VACIA", "La fuente de financiación no puede quedar vacía.");
        }

        Dinero? presupuestoFinal = presupuesto ?? Presupuesto;
        string? fuenteFinal = fuente ?? FuenteFinanciacion;
        Cronograma? cronogramaFinal = cronograma ?? Cronograma;
        if (Estado != EstadoProyecto.Formulado)
        {
            // Después de aprobado, una modificación no puede dejar el proyecto sin las condiciones de aprobación.
            ExigirCondicionesAprobacion(presupuestoFinal, fuenteFinal, Responsable, cronogramaFinal);
        }

        if (presupuesto is not null && presupuesto != Presupuesto)
        {
            Anotar(TipoCambio.Presupuesto, Presupuesto?.ToString(), presupuesto.Value.ToString(), actor.Usuario, justificacion, instante);
            Presupuesto = presupuesto;
        }
        if (fuente is not null && fuente != FuenteFinanciacion)
        {
            Anotar(TipoCambio.Fuente, FuenteFinanciacion, fuente, actor.Usuario, justificacion, instante);
            FuenteFinanciacion = fuente;
        }
        if (cronograma is not null && cronograma != Cronograma)
        {
            Anotar(TipoCambio.Cronograma, Cronograma?.ToString(), cronograma.ToString(), actor.Usuario, justificacion, instante);
            Cronograma = cronograma;
        }
    }

    public void AsignarResponsable(Actor actor, string responsable, string justificacion, DateTimeOffset instante)
    {
        ExigirRol(actor, Rol.Autoridad, "Solo la autoridad asigna el responsable.");
        MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.Asignar);
        ExigirJustificacion(justificacion);
        if (string.IsNullOrWhiteSpace(responsable))
        {
            throw new ReglaDeNegocioException("RESPONSABLE_VACIO", "El responsable no puede quedar vacío.");
        }
        if (responsable == Responsable)
        {
            return;
        }
        Anotar(TipoCambio.Responsable, Responsable, responsable, actor.Usuario, justificacion, instante);
        Responsable = responsable;
    }

    // ------------------------------------------------------------------ transiciones

    public void Aprobar(Actor actor, DateTimeOffset instante)
    {
        ExigirRol(actor, Rol.Autoridad, "Solo la autoridad aprueba proyectos.");
        EstadoProyecto destino = MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.Aprobar);
        ExigirCondicionesAprobacion(Presupuesto, FuenteFinanciacion, Responsable, Cronograma);

        // Jurisdicción: local si el proyecto está en un municipio, regional si abarca varios.
        Ambito requerido = Alcance == AlcanceTerritorial.Municipal ? Ambito.Local : Ambito.Regional;
        if (actor.Ambito != requerido)
        {
            throw new NoAutorizadoException($"Este proyecto lo aprueba la autoridad de ámbito {requerido}.");
        }
        CambiarEstado(destino, actor.Usuario, "Aprobación del proyecto", instante);
    }

    /// <summary>Pasa a En ejecución. Exige contratista: el que llega aquí o uno ya registrado.</summary>
    public void IniciarEjecucion(Actor actor, string? contratista, DateTimeOffset instante)
    {
        ExigirResponsableAsignado(actor);
        EstadoProyecto destino = MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.Iniciar);
        string? contratistaFinal = string.IsNullOrWhiteSpace(contratista) ? Contratista : contratista.Trim();
        if (string.IsNullOrWhiteSpace(contratistaFinal))
        {
            throw new ReglaDeNegocioException("SIN_CONTRATISTA", "Para iniciar la ejecución el proyecto necesita un contratista.");
        }
        if (contratistaFinal != Contratista)
        {
            Anotar(TipoCambio.Contratista, Contratista, contratistaFinal, actor.Usuario, "Registro del contratista", instante);
            Contratista = contratistaFinal;
        }
        CambiarEstado(destino, actor.Usuario, "Inicio de la ejecución", instante);
    }

    public void RegistrarAvance(Actor actor, decimal porcentaje, string? justificacion, DateTimeOffset instante)
    {
        ExigirResponsableAsignado(actor);
        MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.RegistrarAvance);
        if (porcentaje < 0 || porcentaje > 100)
        {
            throw new ReglaDeNegocioException("AVANCE_FUERA_DE_RANGO", "El avance va de 0 a 100 %.");
        }
        if (porcentaje < Avance && string.IsNullOrWhiteSpace(justificacion))
        {
            throw new ReglaDeNegocioException("CORRECCION_SIN_JUSTIFICACION",
                "Un avance menor que el vigente solo se acepta como corrección con justificación.");
        }
        Anotar(TipoCambio.Avance, Texto(Avance), Texto(porcentaje), actor.Usuario, justificacion ?? "Registro de avance", instante);
        _avancesNuevos.Add(new RegistroAvance(Guid.NewGuid(), Id, porcentaje, actor.Usuario, instante, ProcedenciaPlataforma));
        Avance = porcentaje;
    }

    /// <summary>
    /// Avance reportado por el sistema de proyectos externo (EV-24, E-04). Guarda su procedencia, y un valor menor que el
    /// vigente se registra como corrección con la justificación "reportado por" el sistema (regla del Diseño RC). No lo
    /// hace un usuario: no exige el responsable. Fuera de ejecución no aplica y lo dice con una regla incumplida.
    /// </summary>
    public void RegistrarAvanceExterno(string sistema, decimal porcentaje, DateTimeOffset instante)
    {
        MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.RegistrarAvance);
        if (porcentaje < 0 || porcentaje > 100)
        {
            throw new ReglaDeNegocioException("AVANCE_FUERA_DE_RANGO", "El avance va de 0 a 100 %.");
        }
        if (porcentaje == Avance)
        {
            return;
        }
        string justificacion = porcentaje < Avance ? $"Corrección reportada por {sistema}" : $"Avance reportado por {sistema}";
        Anotar(TipoCambio.Avance, Texto(Avance), Texto(porcentaje), sistema, justificacion, instante, sistema);
        _avancesNuevos.Add(new RegistroAvance(Guid.NewGuid(), Id, porcentaje, sistema, instante, sistema));
        Avance = porcentaje;
    }

    /// <summary>Contratista asignado en el sistema de proyectos externo (EV-24, E-04), con su procedencia.</summary>
    public void RegistrarContratistaExterno(string sistema, string contratista, DateTimeOffset instante)
    {
        if (MaquinaEstadosProyecto.EsFinal(Estado))
        {
            throw new ReglaDeNegocioException("ESTADO_FINAL", "Un proyecto en estado final no cambia de contratista.");
        }
        if (string.IsNullOrWhiteSpace(contratista))
        {
            throw new ReglaDeNegocioException("SIN_CONTRATISTA", "El reporte no trae el contratista.");
        }
        string nuevo = contratista.Trim();
        if (nuevo == Contratista)
        {
            return;
        }
        Anotar(TipoCambio.Contratista, Contratista, nuevo, sistema, $"Contratista reportado por {sistema}", instante, sistema);
        Contratista = nuevo;
    }

    public void RegistrarCosto(Actor actor, Dinero monto, string concepto, DateTimeOffset instante)
    {
        ExigirResponsableAsignado(actor);
        MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.RegistrarCosto);
        if (monto.Monto <= 0)
        {
            throw new ReglaDeNegocioException("COSTO_INVALIDO", "Un costo debe ser mayor que cero.");
        }
        if (string.IsNullOrWhiteSpace(concepto))
        {
            throw new ReglaDeNegocioException("COSTO_SIN_CONCEPTO", "Un costo se registra con su concepto.");
        }
        Dinero acumulado = CostoEjecutado + monto;
        Anotar(TipoCambio.Costo, CostoEjecutado.ToString(), acumulado.ToString(), actor.Usuario, concepto, instante);
        _costosNuevos.Add(new RegistroCosto(Guid.NewGuid(), Id, monto, concepto, actor.Usuario, instante));
        // Superar el presupuesto solo marca el sobrecosto (EnSobrecosto); no bloquea el registro.
        CostoEjecutado = acumulado;
    }

    public void Finalizar(Actor actor, DateTimeOffset instante)
    {
        ExigirResponsableAsignado(actor);
        EstadoProyecto destino = MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.Finalizar);
        if (Avance != 100)
        {
            throw new ReglaDeNegocioException("AVANCE_INCOMPLETO", "Un proyecto se finaliza cuando su avance es 100 %.");
        }
        CambiarEstado(destino, actor.Usuario, "Finalización del proyecto", instante);
    }

    public void Cancelar(Actor actor, string justificacion, DateTimeOffset instante)
    {
        ExigirRol(actor, Rol.Autoridad, "Solo la autoridad cancela proyectos.");
        EstadoProyecto destino = MaquinaEstadosProyecto.Destino(Estado, AccionProyecto.Cancelar);
        ExigirJustificacion(justificacion);
        CambiarEstado(destino, actor.Usuario, justificacion, instante);
        // Las evaluaciones quedan libres para otro proyecto; el vínculo se conserva como no vigente.
        foreach (EvaluacionOrigen evaluacion in _evaluaciones)
        {
            evaluacion.Liberar();
        }
    }

    public Documento AdjuntarDocumento(Actor actor, string nombre, string tipo, string ubicacion, string hash, DateTimeOffset instante)
    {
        ExigirResponsableAsignado(actor);
        if (MaquinaEstadosProyecto.EsFinal(Estado))
        {
            throw new ReglaDeNegocioException("ESTADO_FINAL", "Un proyecto en estado final no admite documentos.");
        }
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ReglaDeNegocioException("DOCUMENTO_SIN_NOMBRE", "El documento necesita un nombre.");
        }
        Documento documento = new(Guid.NewGuid(), Id, nombre, tipo, ubicacion, hash, actor.Usuario, instante);
        Anotar(TipoCambio.Documento, null, nombre, actor.Usuario, "Documento adjunto", instante);
        _documentosNuevos.Add(documento);
        return documento;
    }

    // ------------------------------------------------------------------ apoyo

    private void CambiarEstado(EstadoProyecto destino, string autor, string justificacion, DateTimeOffset instante)
    {
        Anotar(TipoCambio.Estado, Estado.ToString(), destino.ToString(), autor, justificacion, instante);
        Estado = destino;
    }

    private void Anotar(TipoCambio tipo, string? anterior, string? nuevo, string autor, string justificacion, DateTimeOffset instante,
        string procedencia = ProcedenciaPlataforma)
    {
        Version++;
        _entradasNuevas.Add(new EntradaHistorial(Guid.NewGuid(), Id, Version, tipo, anterior, nuevo, autor, justificacion, instante, procedencia));
    }

    private void ExigirResponsableAsignado(Actor actor)
    {
        ExigirRol(actor, Rol.Responsable, "Esta operación la hace el responsable del proyecto.");
        if (string.IsNullOrWhiteSpace(Responsable) || actor.Usuario != Responsable)
        {
            throw new NoAutorizadoException("Solo el responsable asignado al proyecto puede hacer esta operación.");
        }
    }

    private static void ExigirRol(Actor actor, Rol rol, string mensaje)
    {
        if (actor.Rol != rol)
        {
            throw new NoAutorizadoException(mensaje);
        }
    }

    private static void ExigirJustificacion(string justificacion)
    {
        if (string.IsNullOrWhiteSpace(justificacion))
        {
            throw new ReglaDeNegocioException("SIN_JUSTIFICACION", "La operación exige una justificación.");
        }
    }

    private static void ExigirCondicionesAprobacion(Dinero? presupuesto, string? fuente, string? responsable, Cronograma? cronograma)
    {
        List<string> faltantes = new();
        if (presupuesto is null || presupuesto.Value.Monto <= 0) faltantes.Add("presupuesto mayor que cero");
        if (string.IsNullOrWhiteSpace(fuente)) faltantes.Add("fuente de financiación");
        if (string.IsNullOrWhiteSpace(responsable)) faltantes.Add("responsable");
        if (cronograma is null) faltantes.Add("cronograma");
        if (faltantes.Count > 0)
        {
            throw new ReglaDeNegocioException("DATOS_INCOMPLETOS", "Al proyecto le falta: " + string.Join(", ", faltantes) + ".");
        }
    }

    private static string Texto(decimal valor) => valor.ToString("0.##", CultureInfo.InvariantCulture);
}
