namespace Reconstruccion.Dominio;

public enum EstadoProyecto { Formulado, Aprobado, EnEjecucion, Finalizado, Cancelado }

public enum AccionProyecto { Asignar, Aprobar, Iniciar, RegistrarAvance, RegistrarCosto, Finalizar, Cancelar }

public enum TipoCambio { Creacion, Presupuesto, Fuente, Responsable, Cronograma, Contratista, Estado, Avance, Costo, Documento }

public enum Rol { Autoridad, Responsable, Auditor }

/// <summary>Ámbito de una autoridad.</summary>
public enum Ambito { Local, Regional, Nacional }

/// <summary>
/// Territorio que cubre un proyecto (DA-14, E-20): un municipio, varios del mismo departamento o municipios de varios
/// departamentos. Nacional es el valor que DA-14 agrega; el canónico ya existía en el contrato (AlcanceProyecto).
/// </summary>
public enum AlcanceTerritorial { Municipal, Regional, Nacional }

public enum NivelDano { SinDano, Leve, Moderado, Grave, DestruccionTotal }

/// <summary>
/// Quién ejecuta una operación. Lo arma la capa de acceso con la identidad de la petición (DA-15).
/// Territorio es el código DIVIPOLA de la autoridad: 5 dígitos si es un municipio, 2 si es un departamento (DA-14).
/// </summary>
public sealed record Actor(string Usuario, Rol Rol, Ambito? Ambito = null, string? Territorio = null);

public static class EstadosDesastre
{
    public const string EnReconstruccion = "EN_RECONSTRUCCION";
}

public static class EstadosEvaluacion
{
    public const string Validada = "VALIDADA";
}
