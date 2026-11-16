namespace EvaluacionDanos.Dominio;

/// <summary>
/// Copia local de un desastre de Gestión de Desastres (E-29, AD-01): se alimenta de EV-03, EV-05, EV-06 y EV-27.
/// Cada dato lleva la versión con la que llegó, de modo que el resultado no depende del orden de llegada: un evento
/// con una versión que no supera la guardada no cambia nada.
/// </summary>
public class Desastre
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";

    /// <summary>Falso mientras no llegue EV-03: se conoce el id por otro evento, pero aún no es un desastre registrado.</summary>
    public bool Registrado { get; set; }

    public EstadoDesastre Estado { get; set; } = EstadoDesastre.DETECTADO;
    public int VersionDesastre { get; set; }

    /// <summary>Municipios (DIVIPOLA) de la delimitación vigente (EV-05), para la descarga por zona (DA-19).</summary>
    public List<string> CodigosMunicipio { get; set; } = [];
    public int ZonaVersion { get; set; }

    /// <summary>Organismo responsable y participantes (EV-27): quiénes pueden validar evaluaciones (AD-22, DA-09).</summary>
    public string? OrganismoResponsable { get; set; }
    public List<string> Organismos { get; set; } = [];
    public int VersionOrganismos { get; set; }

    public bool Cerrado => Estado == EstadoDesastre.CERRADO;

    /// <summary>Un desastre del que solo se conoce el identificador, porque llegó antes otro evento que EV-03.</summary>
    public static Desastre Desconocido(Guid id) => new() { Id = id };

    /// <summary>EV-03. El nombre y el tipo no cambian después; el estado solo si la versión es más nueva.</summary>
    public void AplicarCreacion(string nombre, string tipo, EstadoDesastre estado, int versionDesastre)
    {
        Nombre = Recortar(nombre, Limites.Nombre);
        Tipo = Recortar(tipo, Limites.CodigoTipo);
        Registrado = true;
        AplicarEstado(estado, versionDesastre);
    }

    /// <summary>EV-06 (y el estado inicial de EV-03).</summary>
    public void AplicarEstado(EstadoDesastre estado, int versionDesastre)
    {
        if (versionDesastre > VersionDesastre)
        {
            Estado = estado;
            VersionDesastre = versionDesastre;
        }
    }

    /// <summary>EV-05.</summary>
    public void AplicarZona(int zonaVersion, IEnumerable<string> codigosMunicipio)
    {
        if (zonaVersion > ZonaVersion)
        {
            CodigosMunicipio = codigosMunicipio.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
            ZonaVersion = zonaVersion;
        }
    }

    /// <summary>EV-27.</summary>
    public void AplicarOrganismos(string organismoResponsable, IEnumerable<string> participantes, int version)
    {
        if (version > VersionOrganismos)
        {
            OrganismoResponsable = organismoResponsable.Trim();
            Organismos = participantes.Append(organismoResponsable).Select(o => o.Trim())
                .Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            VersionOrganismos = version;
        }
    }

    /// <summary>La organización es el organismo responsable o uno de los participantes del desastre.</summary>
    public bool Participa(string? organizacion) =>
        !string.IsNullOrWhiteSpace(organizacion)
        && Organismos.Contains(organizacion.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
