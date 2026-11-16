namespace EvaluacionDanos.Dominio;

// Entidades del servicio. El mapeo a tablas está en Infraestructura (Fluent API), para que el dominio no dependa
// de la persistencia (AD-08).

public class TipoActivo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public List<CriterioNivel> Criterios { get; set; } = [];

    /// <summary>Sube con cada cambio del catálogo (AD-28); viaja en EV-25.</summary>
    public int Version { get; set; } = 1;
    public DateTime ActualizadoEn { get; set; }
}

public class CriterioNivel
{
    public string TipoActivoCodigo { get; set; } = "";
    public NivelDano Nivel { get; set; }
    public string Criterio { get; set; } = "";
}

public class Activo
{
    public Guid Id { get; set; }
    public string TipoActivoCodigo { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string Referencia { get; set; } = "";

    /// <summary>Token de concurrencia: sube cada vez que se aplica una evaluación del activo.</summary>
    public int Version { get; set; }

    public string CreadoPorDispositivo { get; set; } = "";

    /// <summary>Municipio (DIVIPOLA) que contiene la ubicación; lo asigna el servidor al crear el activo (DA-07, E-12).</summary>
    public string CodigoMunicipio { get; set; } = "";

    /// <summary>Versión de la capa con la que se asignó el municipio: el activo lo conserva aunque la capa cambie.</summary>
    public int VersionCapa { get; set; }

    /// <summary>Último cambio del activo, para la descarga incremental (DA-19).</summary>
    public DateTime ActualizadoEn { get; set; }

    /// <summary>Si una revisión lo fusionó con otro activo (E-14), el activo que queda. No se borra nada.</summary>
    public Guid? FusionadoEn { get; set; }
}

/// <summary>Qué dispositivo produjo cada versión del activo. Es lo que mira la regla de conflictos.</summary>
public class ActivoVersion
{
    /// <summary>Prefijo del autor de una versión hecha en el servidor (validación, actualización con conexión).</summary>
    public const string PrefijoServidor = "servidor:";

    public Guid ActivoId { get; set; }
    public int Numero { get; set; }
    public string IdDispositivo { get; set; } = "";
    public Guid? EvaluacionId { get; set; }
    public DateTime Instante { get; set; }

    /// <summary>
    /// Autor de un cambio hecho en el servidor. Cuenta como "otro dispositivo" para la regla de conflictos: así una
    /// edición hecha sin conexión sobre una base anterior no sobrescribe en silencio lo que cambió el servidor (AD-32).
    /// </summary>
    public static string AutorServidor(string usuario)
    {
        string texto = PrefijoServidor + usuario;
        return texto.Length <= Limites.IdDispositivo ? texto : texto[..Limites.IdDispositivo];
    }
}

public class Evaluacion
{
    // Los 11 atributos del SRS (Cuadro 9)
    public Guid Id { get; set; }
    public Guid? DesastreId { get; set; }
    public DateTime InstanteCampo { get; set; }
    public string Evaluador { get; set; } = "";
    public string Organizacion { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string TipoActivoCodigo { get; set; } = "";
    public NivelDano NivelDano { get; set; }
    public string Descripcion { get; set; } = "";
    public EstadoEvaluacion Estado { get; set; }

    // Datos de la sincronización
    public Guid ActivoId { get; set; }
    public double PrecisionM { get; set; }
    public DateTime InstanteServidor { get; set; }
    public string IdDispositivo { get; set; } = "";
    public long DesfaseRelojMs { get; set; }

    // Revisión humana
    /// <summary>Quién hizo el último cambio de validación y cuándo (RF-ED-08).</summary>
    public string? ValidadoPor { get; set; }
    public DateTime? InstanteValidacion { get; set; }

    /// <summary>Evaluación que quedó vigente cuando esta se fusionó como duplicado (E-14).</summary>
    public Guid? DuplicadoDe { get; set; }

    /// <summary>Estado de una evaluación sin revisión pendiente: sin desastre es preliminar (AD-29).</summary>
    public EstadoEvaluacion EstadoSinRevision() =>
        DesastreId is null ? EstadoEvaluacion.PRELIMINAR : EstadoEvaluacion.REGISTRADA;

    /// <summary>El contenido actual con la forma de una operación, para guardarlo como versión (AD-30).</summary>
    public EvaluacionEntrante ComoContenido() => new()
    {
        Id = Id,
        ActivoId = ActivoId,
        DesastreId = DesastreId,
        InstanteCampo = new DateTimeOffset(DateTime.SpecifyKind(InstanteCampo, DateTimeKind.Utc)),
        Evaluador = Evaluador,
        Organizacion = Organizacion,
        Lat = Lat,
        Lon = Lon,
        PrecisionM = PrecisionM,
        TipoActivoCodigo = TipoActivoCodigo,
        NivelDano = NivelDano.ToString(),
        Descripcion = Descripcion
    };
}

/// <summary>
/// Historial de solo inserción (RF-ED-07, AD-30). Lo garantiza la base: el rol del servicio solo tiene
/// SELECT e INSERT sobre la tabla (DT-02); además el DbContext rechaza Update y Delete.
/// </summary>
public class EvaluacionVersion
{
    /// <summary>Dispositivo que se registra en una versión creada en el servidor.</summary>
    public const string DispositivoServidor = "servidor";

    public Guid Id { get; set; }
    public Guid EvaluacionId { get; set; }
    public int NumeroVersion { get; set; }
    public string ContenidoJson { get; set; } = "{}";
    public string Autor { get; set; } = "";
    public string IdDispositivo { get; set; } = "";
    public int? VersionActivoBase { get; set; }
    public bool EnConflicto { get; set; }
    public DateTime Instante { get; set; }
    public MotivoVersion Motivo { get; set; } = MotivoVersion.SINCRONIZACION;

    /// <summary>Qué decidió el usuario (estado de validación, decisión del conflicto) y su justificación.</summary>
    public string? Detalle { get; set; }
}

public class Evidencia
{
    public Guid Id { get; set; }
    public Guid EvaluacionId { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public DateTime InstanteCaptura { get; set; }
    public string Sha256Declarado { get; set; } = "";
    public long TamanoBytes { get; set; }
    public bool Subida { get; set; }

    /// <summary>Clave inmutable del objeto en el almacén de archivos (DA-06).</summary>
    public string? ClaveObjeto { get; set; }
    public DateTime? InstanteSubida { get; set; }

    public static string ClaveDe(Guid evaluacionId, Guid evidenciaId) => $"evaluacion/{evaluacionId}/{evidenciaId}";
}

/// <summary>Al fusionar, las evidencias de la evaluación duplicada quedan vinculadas también a la vigente (E-14).</summary>
public class EvidenciaVinculada
{
    public Guid EvaluacionId { get; set; }
    public Guid EvidenciaId { get; set; }
    public Guid ConflictoId { get; set; }
    public DateTime Instante { get; set; }
}

public class Conflicto
{
    public Guid Id { get; set; }
    public Guid ActivoId { get; set; }
    public TipoConflicto Tipo { get; set; }
    public Guid? EvaluacionVigenteId { get; set; }
    public Guid EvaluacionEntranteId { get; set; }
    public Guid VersionEntranteId { get; set; }
    public string Regla { get; set; } = "";
    public EstadoConflicto Estado { get; set; } = EstadoConflicto.PENDIENTE;
    public DateTime Instante { get; set; }

    /// <summary>Estado que tenía la evaluación entrante antes del conflicto, si ya existía (para restituirlo).</summary>
    public EstadoEvaluacion? EstadoPrevioEntrante { get; set; }

    // Decisión humana (AD-32, AD-33): quién, qué y por qué.
    public DecisionConflicto? Decision { get; set; }
    public string? DecididoPor { get; set; }
    public string? Justificacion { get; set; }
    public DateTime? InstanteResolucion { get; set; }
}

/// <summary>Resultado guardado de cada operación: el reenvío devuelve exactamente esto (AD-44).</summary>
public class OperacionProcesada
{
    public Guid IdOperacion { get; set; }
    public string IdDispositivo { get; set; } = "";
    public string ResultadoJson { get; set; } = "{}";
    public DateTime Instante { get; set; }
}
