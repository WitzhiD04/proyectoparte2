namespace GestionDesastres.Dominio;

/// <summary>Niveles de clasificación (SRS RN-3, Cuadro 18).</summary>
public enum Severidad { Nivel1 = 1, Nivel2 = 2, Nivel3 = 3, Nivel4 = 4 }

/// <summary>Los siete estados del ciclo de vida (enunciado §4.1, SRS RN-2).</summary>
public enum EstadoDesastre { Detectado, Registrado, EnEvaluacion, EnAtencion, EnRecuperacion, EnReconstruccion, Cerrado }

/// <summary>Origen de una versión de la delimitación (SRS Cuadro 19).</summary>
public enum FuenteDelimitacion { SistemaGis, EvaluacionCampo, DecisionAutoridad }

/// <summary>Tipos de cambio que deja el historial versionado del desastre (AD-20).</summary>
public enum TipoCambio { Registro, Severidad, Estado, Zona, Organismos, Perfil }

/// <summary>Quién ejecuta una operación, ya traducido desde la identidad de la petición por la capa Api.</summary>
/// <param name="Organizacion">Organismo del usuario. La regla de QA-11 lo compara con el responsable del desastre.</param>
/// <param name="EsAutoridadNacional">Autoridad de ámbito nacional: la única que puede reabrir un desastre cerrado (Cuadro 16).</param>
public sealed record Actor(string Usuario, string? Organizacion, bool EsAutoridad, bool EsOrganismo, bool EsAdministrador,
    bool EsAutoridadNacional)
{
    public bool PuedeOperarDesastres => EsAutoridad || EsOrganismo;
}

/// <summary>Una regla de negocio no se cumple. El código identifica la regla.</summary>
public class ReglaDeNegocioException : Exception
{
    public string Codigo { get; }
    public ReglaDeNegocioException(string codigo, string mensaje) : base(mensaje) { Codigo = codigo; }
}

/// <summary>El actor no tiene permiso para la operación (AD-22, nivel 2).</summary>
public class NoAutorizadoException : Exception
{
    public NoAutorizadoException(string mensaje) : base(mensaje) { }
}

/// <summary>Punto geográfico en grados decimales (WGS 84). Atributo 4 del SRS Cuadro 7.</summary>
public readonly record struct Ubicacion
{
    public double Lat { get; }
    public double Lon { get; }

    public Ubicacion(double lat, double lon)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180 || double.IsNaN(lat) || double.IsNaN(lon))
        {
            throw new ReglaDeNegocioException("UBICACION_INVALIDA", "La ubicación debe tener latitud entre -90 y 90 y longitud entre -180 y 180.");
        }
        Lat = lat;
        Lon = lon;
    }
}

/// <summary>Un organismo que participa en la atención del desastre y su responsabilidad (RF-GD-12).</summary>
public sealed record Participante(string Organismo, string Responsabilidad);
