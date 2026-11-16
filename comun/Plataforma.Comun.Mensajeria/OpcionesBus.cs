using Microsoft.Extensions.Configuration;

namespace Plataforma.Comun.Mensajeria;

/// <summary>Configuración del bus, sección "Bus".</summary>
public sealed class OpcionesBus
{
    public const string Seccion = "Bus";

    /// <summary>Nodos del clúster, como "host" o "host:puerto". El cliente prueba cada uno (AD-02: 3 nodos).</summary>
    /// Empieza vacía: el enlace de configuración agrega a una lista existente en vez de reemplazarla.
    public List<string> Nodos { get; set; } = [];
    public string Usuario { get; set; } = "";
    public string Clave { get; set; } = "";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Nombre del productor en el sobre y en el contador de secuencia.</summary>
    public string Productor { get; set; } = "";

    /// <summary>Cola de la que consume este servicio, si consume.</summary>
    public string? Cola { get; set; }

    public int IntervaloRelevoMs { get; set; } = 1000;
    public int TamanoLote { get; set; } = 50;
    public ushort Prefetch { get; set; } = 20;

    /// <summary>Intentos antes de apartar un mensaje a la cola de mensajes muertos. Igual que el delivery-limit de la política.</summary>
    public int LimiteEntregas { get; set; } = 5;

    /// <summary>
    /// Modo tolerante (E-34), para Auditoría: el sobre se valida siempre, y los datos solo si el tipo y su versión están en
    /// el contrato desplegado. Un tipo nuevo se entrega al manejador en lugar de apartarse, para que no quede un cambio
    /// sin registro (QA-15 b) mientras el consumidor no se actualiza (R-03).
    /// </summary>
    public bool AceptarTiposDesconocidos { get; set; }

    public static OpcionesBus Leer(IConfiguration configuracion)
    {
        OpcionesBus opciones = new();
        configuracion.GetSection(Seccion).Bind(opciones);
        return opciones;
    }
}
