namespace GestionDesastres.Dominio;

/// <summary>Condición que acompaña a una transición admisible (SRS Cuadro 16, columna "Condición y observaciones").</summary>
public enum CondicionTransicion
{
    Ninguna,
    /// <summary>Se registra la justificación (falsa alarma, cierre).</summary>
    ExigeJustificacion,
    /// <summary>En reconstrucción → Cerrado: todos los proyectos asociados alcanzan un estado final.</summary>
    ExigeProyectosFinales,
    /// <summary>Cerrado → En atención: solo la autoridad nacional, con justificación; no forma parte de ningún perfil.</summary>
    Reapertura,
}

/// <summary>Una transición del conjunto máximo admisible.</summary>
public sealed record TransicionAdmisible(EstadoDesastre Origen, EstadoDesastre Destino, CondicionTransicion Condicion, string Observacion);

/// <summary>
/// Conjunto máximo de transiciones (SRS Cuadro 16). Un perfil de ciclo de vida es un subconjunto de esta tabla y ningún
/// perfil puede definir una transición que no esté aquí (RN-2). La máquina de estados es dirigida por datos: el perfil
/// aplicable se consulta en ejecución y las transiciones de un tipo no están escritas en la lógica (QA-10).
/// </summary>
public static class TablaTransiciones
{
    /// <summary>Estado inicial de un registro manual (RF-GD-02). El automático (Detectado, RF-GD-01) es de SE-1, fuera de alcance.</summary>
    public const EstadoDesastre InicialManual = EstadoDesastre.Registrado;

    public static IReadOnlyList<TransicionAdmisible> Todas { get; } =
    [
        new(EstadoDesastre.Detectado, EstadoDesastre.Registrado, CondicionTransicion.Ninguna, "Un usuario autorizado confirma el evento y completa la información mínima"),
        new(EstadoDesastre.Detectado, EstadoDesastre.Cerrado, CondicionTransicion.ExigeJustificacion, "El reporte se descarta por falsa alarma"),
        new(EstadoDesastre.Registrado, EstadoDesastre.EnEvaluacion, CondicionTransicion.Ninguna, "Se despliegan equipos de campo o se solicita una evaluación de daños"),
        new(EstadoDesastre.Registrado, EstadoDesastre.EnAtencion, CondicionTransicion.Ninguna, "El evento requiere respuesta inmediata sin evaluación previa"),
        new(EstadoDesastre.Registrado, EstadoDesastre.Cerrado, CondicionTransicion.ExigeJustificacion, "Ruta corta para un evento de baja gravedad"),
        new(EstadoDesastre.EnEvaluacion, EstadoDesastre.EnAtencion, CondicionTransicion.Ninguna, "Se inicia la respuesta operativa"),
        new(EstadoDesastre.EnEvaluacion, EstadoDesastre.EnRecuperacion, CondicionTransicion.Ninguna, "La evaluación concluye que no hay respuesta de emergencia pendiente"),
        new(EstadoDesastre.EnEvaluacion, EstadoDesastre.Cerrado, CondicionTransicion.ExigeJustificacion, "La evaluación concluye que no hay impacto que atender"),
        new(EstadoDesastre.EnAtencion, EstadoDesastre.EnEvaluacion, CondicionTransicion.Ninguna, "La autoridad ordena una nueva evaluación de daños"),
        new(EstadoDesastre.EnAtencion, EstadoDesastre.EnRecuperacion, CondicionTransicion.Ninguna, "Concluye la fase de respuesta de emergencia"),
        new(EstadoDesastre.EnAtencion, EstadoDesastre.Cerrado, CondicionTransicion.ExigeJustificacion, "El evento se resuelve sin fase de recuperación"),
        new(EstadoDesastre.EnRecuperacion, EstadoDesastre.EnReconstruccion, CondicionTransicion.Ninguna, "Se crean proyectos de reconstrucción (RF-RC-01)"),
        new(EstadoDesastre.EnRecuperacion, EstadoDesastre.Cerrado, CondicionTransicion.ExigeJustificacion, "No se requieren proyectos de reconstrucción"),
        new(EstadoDesastre.EnReconstruccion, EstadoDesastre.Cerrado, CondicionTransicion.ExigeProyectosFinales, "Todos los proyectos asociados alcanzan un estado final"),
        new(EstadoDesastre.Cerrado, EstadoDesastre.EnAtencion, CondicionTransicion.Reapertura, "Reapertura ordenada por la autoridad nacional"),
    ];

    public static TransicionAdmisible? Buscar(EstadoDesastre origen, EstadoDesastre destino) =>
        Todas.FirstOrDefault(t => t.Origen == origen && t.Destino == destino);
}

/// <summary>
/// Perfil de ciclo de vida (SRS RN-2, Cuadro 17): el conjunto de estados que recorre un tipo de desastre. Sus
/// transiciones son las del Cuadro 16 entre esos estados, sin la reapertura. El rango ordena los perfiles para el cambio
/// de perfil al reclasificar (Simplificado &lt; Estándar &lt; Extendido).
/// </summary>
public sealed class PerfilCicloVida
{
    public string Codigo { get; }
    public string Nombre { get; }
    public int Rango { get; }
    public IReadOnlySet<EstadoDesastre> Estados { get; }

    public PerfilCicloVida(string codigo, string nombre, int rango, IEnumerable<EstadoDesastre> estados)
    {
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre))
        {
            throw new ReglaDeNegocioException("PERFIL_INVALIDO", "Un perfil necesita código y nombre.");
        }
        HashSet<EstadoDesastre> conjunto = estados.ToHashSet();
        if (!conjunto.Contains(TablaTransiciones.InicialManual) || !conjunto.Contains(EstadoDesastre.Cerrado))
        {
            throw new ReglaDeNegocioException("PERFIL_INVALIDO", "Todo perfil incluye el estado inicial Registrado y el estado final Cerrado.");
        }
        Codigo = codigo.Trim().ToUpperInvariant();
        Nombre = nombre.Trim();
        Rango = rango;
        Estados = conjunto;
    }

    /// <summary>Transiciones del perfil: las del Cuadro 16 cuyos dos estados están en el perfil (nunca la reapertura).</summary>
    public IReadOnlyList<TransicionAdmisible> Transiciones =>
        TablaTransiciones.Todas
            .Where(t => t.Condicion != CondicionTransicion.Reapertura && Estados.Contains(t.Origen) && Estados.Contains(t.Destino))
            .ToList();

    public bool Admite(EstadoDesastre origen, EstadoDesastre destino) =>
        Transiciones.Any(t => t.Origen == origen && t.Destino == destino);

    // Perfiles propuestos por el SRS (Cuadro 17). La organización puede definir otros (QA-10).
    public static PerfilCicloVida Simplificado() => new("SIMPLIFICADO", "Simplificado", 1,
        [EstadoDesastre.Registrado, EstadoDesastre.Cerrado]);

    public static PerfilCicloVida Estandar() => new("ESTANDAR", "Estándar", 2,
        [EstadoDesastre.Detectado, EstadoDesastre.Registrado, EstadoDesastre.EnEvaluacion, EstadoDesastre.EnAtencion,
         EstadoDesastre.EnRecuperacion, EstadoDesastre.Cerrado]);

    public static PerfilCicloVida Extendido() => new("EXTENDIDO", "Extendido", 3, Enum.GetValues<EstadoDesastre>());
}

/// <summary>
/// Perfil mínimo que exige cada nivel (dato configurable, QA-10). RN-2 dice que al reclasificar a un nivel superior el
/// desastre cambia de perfil si el suyo "no admite las fases que el nuevo nivel exige", sin definir esas fases.
/// <b>No está en los documentos:</b> el usuario aprobó usar un perfil mínimo por nivel, con valores iniciales tomados de la
/// "aplicación habitual" del Cuadro 17 (Nivel_1 Simplificado, Nivel_2 Estándar, Nivel_3 y Nivel_4 Extendido).
/// </summary>
public sealed class PerfilPorNivel
{
    private readonly Dictionary<Severidad, string> _minimos;

    public PerfilPorNivel(IReadOnlyDictionary<Severidad, string> minimos)
    {
        if (Enum.GetValues<Severidad>().Any(n => !minimos.ContainsKey(n)))
        {
            throw new ReglaDeNegocioException("PERFIL_POR_NIVEL_INCOMPLETO", "Cada nivel necesita su perfil mínimo.");
        }
        _minimos = minimos.ToDictionary(p => p.Key, p => p.Value.Trim().ToUpperInvariant());
    }

    public string MinimoPara(Severidad nivel) => _minimos[nivel];

    public IReadOnlyDictionary<Severidad, string> Minimos => _minimos;

    public static PerfilPorNivel Inicial() => new(new Dictionary<Severidad, string>
    {
        [Severidad.Nivel1] = "SIMPLIFICADO",
        [Severidad.Nivel2] = "ESTANDAR",
        [Severidad.Nivel3] = "EXTENDIDO",
        [Severidad.Nivel4] = "EXTENDIDO",
    });
}
