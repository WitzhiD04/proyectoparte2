namespace Reconstruccion.Aplicacion.Puertos;

/// <summary>Puerto de entrada: lo que un controller conoce de un caso de uso.</summary>
public interface IPuertoEntrada<TSolicitud, TRespuesta>
{
    Task EjecutarAsync(TSolicitud solicitud, IPuertoSalida<TRespuesta> salida, CancellationToken ct = default);
}

/// <summary>Puerto de salida: por aquí el caso de uso entrega su resultado. Lo implementa un presenter.</summary>
public interface IPuertoSalida<TRespuesta>
{
    void Exito(TRespuesta respuesta);
    void NoEncontrado(string mensaje);
    void NoAutorizado(string mensaje);
    void ReglaIncumplida(string codigo, string mensaje, IReadOnlyList<string>? detalles = null);
}
