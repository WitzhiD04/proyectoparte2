namespace GestionDesastres.Dominio;

/// <summary>Estado del desastre justo después de un cambio: severidad, estado, perfil y organismos.</summary>
public sealed record EstadoResultante(Severidad Severidad, EstadoDesastre Estado, string CodigoPerfil, string Responsable,
    IReadOnlyList<Participante> Participantes);

/// <summary>
/// Una entrada del historial versionado (AD-20): un cambio con su valor anterior y nuevo, autor y justificación, y el
/// estado resultante. El historial es el dato principal: el estado vigente es el de la última entrada y la evolución se
/// lee entrada por entrada, sin una copia aparte del estado que pueda desincronizarse (la alternativa que AD-20 descarta).
/// </summary>
public sealed record CambioDesastre(
    Guid IdDesastre,
    int Version,
    TipoCambio Tipo,
    string? ValorAnterior,
    string? ValorNuevo,
    string Autor,
    string? Organizacion,
    string Justificacion,
    DateTimeOffset Instante,
    EstadoResultante Resultado);

/// <summary>Datos planos para guardar y volver a cargar un desastre.</summary>
public sealed record InstantaneaDesastre(
    Guid Id,
    string CodigoTipo,
    string Nombre,
    string Descripcion,
    DateTimeOffset InstanteInicio,
    Ubicacion Ubicacion,
    IReadOnlyList<string> OrganismosRegistra,
    IReadOnlyList<string> Fuentes,
    string Responsable,
    IReadOnlyList<Participante> Participantes,
    Severidad Severidad,
    EstadoDesastre Estado,
    string CodigoPerfil,
    IReadOnlyList<VersionZona> Zonas,
    int Version,
    DateTimeOffset InstanteActualizacion);

/// <summary>
/// Evento de desastre (SRS módulo 1). Guarda los doce atributos del Cuadro 7 y aplica las reglas RN-1 a RN-4 y la de
/// autorización por organismo responsable (QA-11, AD-22). Cada operación deja su entrada en el historial versionado y
/// sube la versión; si una regla no se cumple, lanza y no cambia nada.
/// </summary>
public sealed class Desastre
{
    private readonly List<string> _organismosRegistra = new();
    private readonly List<string> _fuentes = new();
    private readonly List<Participante> _participantes = new();
    private readonly List<VersionZona> _zonas = new();
    private readonly List<CambioDesastre> _cambiosNuevos = new();

    public Guid Id { get; private set; }
    public string CodigoTipo { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public DateTimeOffset InstanteInicio { get; private set; }
    public Ubicacion Ubicacion { get; private set; }
    public IReadOnlyList<string> OrganismosRegistra => _organismosRegistra;
    /// <summary>Fuente de la información (atributo 8): en un registro manual, el usuario que lo registró.</summary>
    public IReadOnlyList<string> Fuentes => _fuentes;
    /// <summary>Responsable de la emergencia (atributo 10). Se guarda el código del organismo, sin datos de contacto.</summary>
    public string Responsable { get; private set; } = "";
    public IReadOnlyList<Participante> Participantes => _participantes;
    public Severidad Severidad { get; private set; }
    public EstadoDesastre Estado { get; private set; }
    public string CodigoPerfil { get; private set; } = "";
    public IReadOnlyList<VersionZona> Zonas => _zonas;
    public VersionZona ZonaVigente => _zonas[^1];
    public int Version { get; private set; }
    /// <summary>Atributo 12: instante de la última modificación.</summary>
    public DateTimeOffset InstanteActualizacion { get; private set; }
    public IReadOnlyList<CambioDesastre> CambiosNuevos => _cambiosNuevos;

    private Desastre() { }

    // ------------------------------------------------------------------ registro (RF-GD-02, RF-GD-03, RF-GD-04)

    public static Desastre Registrar(
        Guid id, TipoDesastre tipo, PerfilCicloVida perfilDelTipo, PerfilCicloVida perfilMinimoDelNivel,
        string nombre, string descripcion, DateTimeOffset instanteInicio, Ubicacion ubicacion, Severidad severidad,
        string responsable, IReadOnlyList<Participante> participantes,
        string? geometriaZona, IReadOnlyList<string> municipiosZona, FuenteDelimitacion fuenteZona,
        Actor actor, DateTimeOffset instante)
    {
        if (!actor.PuedeOperarDesastres)
        {
            throw new NoAutorizadoException("Solo un organismo de respuesta o una autoridad registra desastres (RF-GD-02).");
        }
        if (string.IsNullOrWhiteSpace(actor.Organizacion))
        {
            throw new ReglaDeNegocioException("SIN_ORGANISMO", "El organismo que registra el evento es obligatorio (Cuadro 7, atributo 9).");
        }
        if (!tipo.EsVigente(instante))
        {
            throw new ReglaDeNegocioException("TIPO_NO_VIGENTE", $"El tipo {tipo.Codigo} no está vigente para registrar eventos nuevos (RF-GD-04).");
        }
        ExigirTexto(nombre, "nombre");
        ExigirTexto(descripcion, "descripción del evento (atributo 7)");
        ExigirTexto(responsable, "responsable de la emergencia (atributo 10)");
        if (instanteInicio > instante.AddMinutes(5))
        {
            throw new ReglaDeNegocioException("INICIO_FUTURO", "La fecha y hora de inicio no puede ser futura (atributo 3).");
        }
        VersionZona.Validar(geometriaZona, municipiosZona);

        Desastre d = new()
        {
            Id = id,
            CodigoTipo = tipo.Codigo,
            Nombre = nombre.Trim(),
            Descripcion = descripcion.Trim(),
            InstanteInicio = instanteInicio,
            Ubicacion = ubicacion,
            Responsable = responsable.Trim(),
            Severidad = severidad,
            Estado = TablaTransiciones.InicialManual,
            // RN-2: el perfil es el del tipo, salvo que el nivel exija uno superior.
            CodigoPerfil = perfilMinimoDelNivel.Rango > perfilDelTipo.Rango ? perfilMinimoDelNivel.Codigo : perfilDelTipo.Codigo,
        };
        d._organismosRegistra.Add(actor.Organizacion.Trim());
        d._fuentes.Add("usuario:" + actor.Usuario);
        // RF-GD-12: se proponen los organismos que el tipo y el nivel definen por defecto; la autoridad puede ajustarlos.
        d._participantes.AddRange(Normalizar(participantes.Count > 0 ? participantes : tipo.OrganismosPorDefecto(severidad), d.Responsable));
        d._zonas.Add(new VersionZona(1, instante, geometriaZona, municipiosZona.Distinct().OrderBy(c => c).ToList(), fuenteZona,
            actor.Usuario, "Delimitación inicial"));
        d.Anotar(TipoCambio.Registro, null, $"{tipo.Codigo}|{severidad}|{d.Estado}|{d.CodigoPerfil}", actor, "Registro manual del evento", instante);
        return d;
    }

    // ------------------------------------------------------------------ reclasificación (RF-GD-06, RN-3)

    /// <summary>
    /// Cambia el nivel. Si el nuevo nivel exige un perfil de rango mayor que el actual, el desastre cambia a ese perfil y
    /// conserva los estados ya recorridos (RN-2). Un descenso de nivel no baja el perfil.
    /// </summary>
    public void Reclasificar(Severidad nueva, string justificacion, PerfilCicloVida perfilActual, PerfilCicloVida perfilMinimoDelNivel,
        Actor actor, DateTimeOffset instante)
    {
        ExigirResponsable(actor);
        ExigirAbierto();
        ExigirTexto(justificacion, "justificación de la reclasificación");
        if (nueva == Severidad)
        {
            throw new ReglaDeNegocioException("SIN_CAMBIO", $"El desastre ya está en {nueva}.");
        }
        Severidad anterior = Severidad;
        Severidad = nueva;
        Anotar(TipoCambio.Severidad, anterior.ToString(), nueva.ToString(), actor, justificacion, instante);
        if (perfilMinimoDelNivel.Rango > perfilActual.Rango)
        {
            string perfilAnterior = CodigoPerfil;
            CodigoPerfil = perfilMinimoDelNivel.Codigo;
            Anotar(TipoCambio.Perfil, perfilAnterior, CodigoPerfil, actor,
                $"El {nueva} exige el perfil {perfilMinimoDelNivel.Nombre} (RN-2)", instante);
        }
    }

    // ------------------------------------------------------------------ transiciones (RF-GD-07, RF-GD-18)

    /// <param name="proyectosNoFinales">Proyectos de reconstrucción del desastre que no están en estado final (copia local de EV-18 y EV-20).</param>
    public void CambiarEstado(EstadoDesastre destino, string? justificacion, PerfilCicloVida perfilActual, int proyectosNoFinales,
        Actor actor, DateTimeOffset instante)
    {
        TransicionAdmisible? transicion = TablaTransiciones.Buscar(Estado, destino);
        if (transicion is null)
        {
            throw new ReglaDeNegocioException("TRANSICION_NO_PERMITIDA", $"No existe la transición de {Estado} a {destino} (SRS Cuadro 16).");
        }

        if (transicion.Condicion == CondicionTransicion.Reapertura)
        {
            // La reapertura no forma parte de ningún perfil: solo la ordena la autoridad nacional, con justificación.
            if (!actor.EsAutoridadNacional)
            {
                throw new NoAutorizadoException("Solo la autoridad nacional puede reabrir un desastre cerrado.");
            }
        }
        else
        {
            ExigirResponsable(actor);
            if (!perfilActual.Admite(Estado, destino))
            {
                throw new ReglaDeNegocioException("TRANSICION_NO_PERMITIDA",
                    $"El perfil {perfilActual.Nombre} no admite la transición de {Estado} a {destino} (RN-2).");
            }
        }
        if (transicion.Condicion is CondicionTransicion.ExigeJustificacion or CondicionTransicion.Reapertura
            || destino == EstadoDesastre.Cerrado)
        {
            ExigirTexto(justificacion, "justificación");
        }
        if (transicion.Condicion == CondicionTransicion.ExigeProyectosFinales && proyectosNoFinales > 0)
        {
            throw new ReglaDeNegocioException("PROYECTOS_ABIERTOS",
                $"No se puede cerrar: hay {proyectosNoFinales} proyectos de reconstrucción que no están en un estado final.");
        }

        EstadoDesastre anterior = Estado;
        Estado = destino;
        Anotar(TipoCambio.Estado, anterior.ToString(), destino.ToString(), actor, justificacion ?? transicion.Observacion, instante);
    }

    // ------------------------------------------------------------------ delimitación (RF-GD-08, RF-GD-09, RN-4)

    public VersionZona Delimitar(string? geometria, IReadOnlyList<string> municipios, FuenteDelimitacion fuente, string justificacion,
        Actor actor, DateTimeOffset instante)
    {
        ExigirResponsable(actor);
        ExigirAbierto();
        ExigirTexto(justificacion, "justificación del cambio de delimitación (Cuadro 19)");
        VersionZona.Validar(geometria, municipios);
        VersionZona nueva = new(ZonaVigente.Version + 1, instante, geometria, municipios.Distinct().OrderBy(c => c).ToList(), fuente,
            actor.Usuario, justificacion.Trim());
        _zonas.Add(nueva);
        Anotar(TipoCambio.Zona, $"v{nueva.Version - 1}", $"v{nueva.Version}", actor, justificacion, instante);
        return nueva;
    }

    // ------------------------------------------------------------------ organismos (RF-GD-12)

    public void AsignarOrganismos(string responsable, IReadOnlyList<Participante> participantes, string justificacion, Actor actor,
        DateTimeOffset instante)
    {
        ExigirResponsable(actor);
        ExigirAbierto();
        ExigirTexto(responsable, "responsable de la emergencia");
        ExigirTexto(justificacion, "justificación");
        string anterior = Resumen(Responsable, _participantes);
        Responsable = responsable.Trim();
        _participantes.Clear();
        _participantes.AddRange(Normalizar(participantes, Responsable));
        Anotar(TipoCambio.Organismos, anterior, Resumen(Responsable, _participantes), actor, justificacion, instante);
    }

    // ------------------------------------------------------------------ carga y apoyo

    public static Desastre Rehidratar(InstantaneaDesastre d)
    {
        Desastre desastre = new()
        {
            Id = d.Id, CodigoTipo = d.CodigoTipo, Nombre = d.Nombre, Descripcion = d.Descripcion, InstanteInicio = d.InstanteInicio,
            Ubicacion = d.Ubicacion, Responsable = d.Responsable, Severidad = d.Severidad, Estado = d.Estado, CodigoPerfil = d.CodigoPerfil,
            Version = d.Version, InstanteActualizacion = d.InstanteActualizacion,
        };
        desastre._organismosRegistra.AddRange(d.OrganismosRegistra);
        desastre._fuentes.AddRange(d.Fuentes);
        desastre._participantes.AddRange(d.Participantes);
        desastre._zonas.AddRange(d.Zonas.OrderBy(z => z.Version));
        return desastre;
    }

    public InstantaneaDesastre AInstantanea() => new(Id, CodigoTipo, Nombre, Descripcion, InstanteInicio, Ubicacion,
        _organismosRegistra.ToList(), _fuentes.ToList(), Responsable, _participantes.ToList(), Severidad, Estado, CodigoPerfil,
        _zonas.ToList(), Version, InstanteActualizacion);

    public void LimpiarPendientes() => _cambiosNuevos.Clear();

    /// <summary>
    /// Regla de QA-11 (nivel 2 de AD-22): cualquier usuario con el rol adecuado consulta, pero solo el organismo
    /// responsable asignado modifica la severidad, la zona, el estado o los organismos del desastre.
    /// </summary>
    private void ExigirResponsable(Actor actor)
    {
        if (!actor.PuedeOperarDesastres)
        {
            throw new NoAutorizadoException("Solo un organismo de respuesta o una autoridad modifica un desastre.");
        }
        if (!string.Equals(actor.Organizacion, Responsable, StringComparison.Ordinal))
        {
            throw new NoAutorizadoException($"Solo el organismo responsable ({Responsable}) puede modificar este desastre.");
        }
    }

    private void ExigirAbierto()
    {
        if (Estado == EstadoDesastre.Cerrado)
        {
            throw new ReglaDeNegocioException("DESASTRE_CERRADO", "El desastre está cerrado: no admite cambios (salvo la reapertura).");
        }
    }

    private void Anotar(TipoCambio tipo, string? anterior, string? nuevo, Actor actor, string justificacion, DateTimeOffset instante)
    {
        Version++;
        InstanteActualizacion = instante;
        _cambiosNuevos.Add(new CambioDesastre(Id, Version, tipo, anterior, nuevo, actor.Usuario, actor.Organizacion, justificacion.Trim(), instante,
            new EstadoResultante(Severidad, Estado, CodigoPerfil, Responsable, _participantes.ToList())));
    }

    private static IEnumerable<Participante> Normalizar(IEnumerable<Participante> participantes, string responsable)
    {
        List<Participante> lista = participantes
            .Where(p => !string.IsNullOrWhiteSpace(p.Organismo))
            .GroupBy(p => p.Organismo.Trim())
            .Select(g => new Participante(g.Key, g.First().Responsabilidad.Trim()))
            .ToList();
        // El responsable siempre figura entre los participantes.
        if (lista.All(p => p.Organismo != responsable))
        {
            lista.Insert(0, new Participante(responsable, "Responsable de la emergencia"));
        }
        return lista;
    }

    private static string Resumen(string responsable, IEnumerable<Participante> participantes) =>
        $"{responsable}|{string.Join(",", participantes.Select(p => p.Organismo))}";

    private static void ExigirTexto(string? valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ReglaDeNegocioException("DATO_OBLIGATORIO", $"Falta el dato obligatorio: {campo}.");
        }
    }
}
