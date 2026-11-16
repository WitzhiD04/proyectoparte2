using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion;

/// <summary>Puerto de entrada: lo que un controller conoce de un caso de uso.</summary>
public interface ICasoDeUso<in TSolicitud, TRespuesta>
{
    Task<Resultado<TRespuesta>> EjecutarAsync(TSolicitud solicitud, CancellationToken cancelar = default);
}

public enum TipoFallo
{
    /// <summary>El dato enviado no es válido.</summary>
    Invalido,
    NoEncontrado,
    /// <summary>La regla de dominio no autoriza a este usuario (AD-22, nivel 2).</summary>
    NoAutorizado,
    /// <summary>La regla de negocio no lo permite en el estado actual.</summary>
    ReglaIncumplida,
    /// <summary>Otro cambio llegó primero: hay que volver a leer y decidir de nuevo.</summary>
    Concurrencia,
    /// <summary>Falta una dependencia para atender ahora; se puede reintentar sin perder nada.</summary>
    NoDisponible
}

/// <summary>Lo que entrega un caso de uso: la respuesta o por qué no se pudo. La capa de entrada lo presenta.</summary>
public sealed record Resultado<T>(T? Valor, TipoFallo? Fallo, string? Mensaje)
{
    public bool Exito => Fallo is null;

    public static Resultado<T> Ok(T valor) => new(valor, null, null);

    public static Resultado<T> Falla(TipoFallo fallo, string mensaje) => new(default, fallo, mensaje);

    public static Resultado<T> De(Rechazo rechazo) => Falla(rechazo.Tipo switch
    {
        TipoRechazo.Invalido => TipoFallo.Invalido,
        TipoRechazo.NoAutorizado => TipoFallo.NoAutorizado,
        _ => TipoFallo.ReglaIncumplida
    }, rechazo.Mensaje);
}

/// <summary>
/// Quién pide la operación. Lo arma la capa de entrada con la identidad ya autenticada; el rol (nivel 1) se revisa
/// allí y las reglas que dependen de los datos (nivel 2, AD-22) las aplican el caso de uso y el dominio.
/// </summary>
public sealed record Actor(string Usuario, string? Organizacion, string? IdDispositivo, bool EsOperador = false);

/// <summary>Parámetros de operación del servicio (secciones Duplicados, Sincronizacion y Evidencias de la configuración).</summary>
public sealed class Parametros
{
    /// <summary>Distancia máxima entre dos activos del mismo tipo para marcarlos como posible duplicado (AD-33).</summary>
    public double DistanciaDuplicadoMetros { get; set; } = 25;

    /// <summary>Ventana de tiempo, hacia ambos lados del instante en campo, de la regla de duplicados (RF-ED-09).</summary>
    public double VentanaDuplicadoHoras { get; set; } = 24;

    public int MaxOperacionesPorLote { get; set; } = 100;

    public int TamanoMaximoEvidenciaMb { get; set; } = 10;

    /// <summary>Traslape de la descarga incremental: cubre los cambios confirmados después de leer el reloj (DA-19).</summary>
    public int MargenDescargaSegundos { get; set; } = 60;
}
