namespace Reconstruccion.Dominio;

public enum EstadoProyecto { Formulado, Aprobado, EnEjecucion, Finalizado, Cancelado }

public enum AccionProyecto { Asignar, Aprobar, Iniciar, RegistrarAvance, RegistrarCosto, Finalizar, Cancelar }

public enum TipoCambio { Creacion, Presupuesto, Fuente, Responsable, Cronograma, Contratista, Estado, Avance, Costo, Documento }

public enum Rol { Autoridad, Responsable, Auditor }

/// <summary>Ámbito de una autoridad.</summary>
public enum Ambito { Local, Regional, Nacional }

/// <summary>Territorio que cubre un proyecto: un municipio o varios.</summary>
public enum AlcanceTerritorial { Municipal, Regional }

public enum NivelDano { SinDano, Leve, Moderado, Grave, DestruccionTotal }

/// <summary>Quién ejecuta una operación. Lo arma la capa de acceso con los datos de la petición.</summary>
public sealed record Actor(string Usuario, Rol Rol, Ambito? Ambito = null);

public static class EstadosDesastre
{
    public const string EnReconstruccion = "EN_RECONSTRUCCION";
}

public static class EstadosEvaluacion
{
    public const string Validada = "VALIDADA";
}
