namespace GestionDesastres.Dominio;

/// <summary>
/// Entrada del catálogo de tipos de desastre (SRS RN-1, Cuadro 15). Es contenido administrado en ejecución: incorporar o
/// retirar un tipo no modifica ni redespliega componentes (QA-10, AD-07). Un tipo retirado no se borra: deja de estar
/// vigente para eventos nuevos y los históricos conservan su referencia.
/// Fuera de alcance del servicio mínimo: las categorías de daño aplicables (las administra Evaluación de Daños, AD-28) y
/// las categorías de recursos por defecto (Gestión de Recursos, fuera de alcance).
/// </summary>
public sealed class TipoDesastre
{
    public string Codigo { get; }
    public string Nombre { get; private set; }
    public string CodigoPerfil { get; private set; }
    /// <summary>Criterios de clasificación por nivel (RN-3): texto que la organización define por tipo (S-3).</summary>
    public IReadOnlyDictionary<Severidad, string> CriteriosPorNivel { get; private set; }
    /// <summary>Organismos convocados por defecto, por nivel (RF-GD-12). La autoridad puede ajustarlos en cada evento.</summary>
    public IReadOnlyDictionary<Severidad, IReadOnlyList<Participante>> OrganismosPorNivel { get; private set; }
    public DateTimeOffset VigenteDesde { get; private set; }
    public DateTimeOffset? VigenteHasta { get; private set; }

    public TipoDesastre(string codigo, string nombre, string codigoPerfil, IReadOnlyDictionary<Severidad, string> criterios,
        IReadOnlyDictionary<Severidad, IReadOnlyList<Participante>> organismos, DateTimeOffset vigenteDesde, DateTimeOffset? vigenteHasta = null)
    {
        if (string.IsNullOrWhiteSpace(codigo) || !codigo.Trim().All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ReglaDeNegocioException("TIPO_INVALIDO", "El código de un tipo es estable y en mayúsculas (por ejemplo TERREMOTO).");
        }
        Codigo = codigo.Trim();
        Nombre = "";
        CodigoPerfil = "";
        CriteriosPorNivel = new Dictionary<Severidad, string>();
        OrganismosPorNivel = new Dictionary<Severidad, IReadOnlyList<Participante>>();
        Modificar(nombre, codigoPerfil, criterios, organismos, vigenteDesde, vigenteHasta);
    }

    /// <summary>El código es inmutable una vez creado (Cuadro 15); lo demás se administra en ejecución.</summary>
    public void Modificar(string nombre, string codigoPerfil, IReadOnlyDictionary<Severidad, string> criterios,
        IReadOnlyDictionary<Severidad, IReadOnlyList<Participante>> organismos, DateTimeOffset vigenteDesde, DateTimeOffset? vigenteHasta)
    {
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(codigoPerfil))
        {
            throw new ReglaDeNegocioException("TIPO_INVALIDO", "Un tipo necesita nombre y perfil de ciclo de vida.");
        }
        if (vigenteHasta is not null && vigenteHasta <= vigenteDesde)
        {
            throw new ReglaDeNegocioException("TIPO_INVALIDO", "El fin de la vigencia debe ser posterior a su inicio.");
        }
        Nombre = nombre.Trim();
        CodigoPerfil = codigoPerfil.Trim().ToUpperInvariant();
        CriteriosPorNivel = criterios.ToDictionary(p => p.Key, p => p.Value);
        OrganismosPorNivel = organismos.ToDictionary(p => p.Key, p => (IReadOnlyList<Participante>)p.Value.ToList());
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
    }

    /// <summary>Un tipo solo se usa para registrar eventos nuevos mientras está vigente (RF-GD-04).</summary>
    public bool EsVigente(DateTimeOffset instante) => instante >= VigenteDesde && (VigenteHasta is null || instante < VigenteHasta);

    public IReadOnlyList<Participante> OrganismosPorDefecto(Severidad nivel) =>
        OrganismosPorNivel.TryGetValue(nivel, out IReadOnlyList<Participante>? lista) ? lista : [];
}
