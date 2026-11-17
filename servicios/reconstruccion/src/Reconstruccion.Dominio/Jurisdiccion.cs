namespace Reconstruccion.Dominio;

/// <summary>
/// Jurisdicción territorial de las autoridades (DA-14, E-20). Usa el código DIVIPOLA: un municipio tiene 5 dígitos y
/// los 2 primeros son su departamento. Por eso el departamento se deduce del municipio y no hace falta otra copia local.
/// </summary>
public static class Jurisdiccion
{
    private const int DigitosDepartamento = 2;

    /// <summary>Departamento (2 dígitos) de un municipio DIVIPOLA (5 dígitos).</summary>
    public static string DepartamentoDe(string codigoMunicipio) =>
        codigoMunicipio.Length >= DigitosDepartamento ? codigoMunicipio[..DigitosDepartamento] : codigoMunicipio;

    /// <summary>
    /// Alcance de un proyecto según los municipios de sus evaluaciones: uno solo es municipal, varios del mismo
    /// departamento es regional y municipios de varios departamentos es nacional (DA-14).
    /// </summary>
    public static AlcanceTerritorial AlcanceDe(IEnumerable<string> codigosMunicipio)
    {
        List<string> municipios = codigosMunicipio.Distinct().ToList();
        if (municipios.Count <= 1)
        {
            return AlcanceTerritorial.Municipal;
        }
        return municipios.Select(DepartamentoDe).Distinct().Count() == 1 ? AlcanceTerritorial.Regional : AlcanceTerritorial.Nacional;
    }

    /// <summary>
    /// Regla de aprobación (DA-14): el municipal lo aprueba la autoridad LOCAL de ese municipio, el regional la REGIONAL
    /// de ese departamento y el nacional la NACIONAL. Si no le corresponde, lanza <see cref="NoAutorizadoException"/>.
    /// </summary>
    public static void ExigirAprobador(Actor actor, AlcanceTerritorial alcance, IReadOnlyList<string> codigosMunicipio)
    {
        switch (alcance)
        {
            case AlcanceTerritorial.Municipal:
                string? municipio = codigosMunicipio.Count == 1 ? codigosMunicipio[0] : null;
                if (actor.Ambito != Ambito.Local || municipio is null || actor.Territorio != municipio)
                {
                    throw new NoAutorizadoException($"Este proyecto lo aprueba la autoridad LOCAL del municipio {municipio}.");
                }
                break;
            case AlcanceTerritorial.Regional:
                List<string> departamentos = codigosMunicipio.Select(DepartamentoDe).Distinct().ToList();
                string? departamento = departamentos.Count == 1 ? departamentos[0] : null;
                if (actor.Ambito != Ambito.Regional || departamento is null || actor.Territorio != departamento)
                {
                    throw new NoAutorizadoException($"Este proyecto lo aprueba la autoridad REGIONAL del departamento {departamento}.");
                }
                break;
            case AlcanceTerritorial.Nacional:
                if (actor.Ambito != Ambito.Nacional)
                {
                    throw new NoAutorizadoException("Este proyecto abarca varios departamentos: lo aprueba la autoridad NACIONAL.");
                }
                break;
            default:
                throw new NoAutorizadoException($"Alcance sin regla de aprobación: {alcance}.");
        }
    }

    /// <summary>
    /// Las acciones de una autoridad sobre un proyecto (asignar, aprobar, cancelar) exigen que el proyecto sea de su
    /// jurisdicción. Si no lo es, lanza <see cref="FueraDeJurisdiccionException"/>, que la aplicación responde como un
    /// proyecto que no existe: quien no ve el proyecto no debe saber que existe (Cuadro 3 del SRS, E-39). Se revisa antes
    /// que cualquier otra regla, para que una regla incumplida no revele nada del proyecto.
    /// </summary>
    public static void ExigirQueLoVe(Actor actor, IReadOnlyList<string> codigosMunicipio)
    {
        if (!Cubre(actor, codigosMunicipio))
        {
            throw new FueraDeJurisdiccionException();
        }
    }

    /// <summary>Si un municipio está dentro de lo que el actor puede ver (misma regla que <see cref="Cubre"/>).</summary>
    public static bool CubreMunicipio(Actor actor, string codigoMunicipio) => Cubre(actor, [codigoMunicipio]);

    /// <summary>
    /// Una autoridad solo crea proyectos con evaluaciones de su jurisdicción (Cuadro 3 del SRS, DA-14): cada municipio
    /// tiene que estar en su territorio, no basta con que uno lo esté. Si alguno queda fuera lanza
    /// <see cref="NoAutorizadoException"/> y no se crea nada.
    /// </summary>
    public static void ExigirJurisdiccionSobre(Actor actor, IEnumerable<string> codigosMunicipio)
    {
        List<string> fuera = codigosMunicipio.Distinct().Where(m => !CubreMunicipio(actor, m)).OrderBy(m => m).ToList();
        if (fuera.Count > 0)
        {
            throw new NoAutorizadoException(
                "Hay evaluaciones de municipios fuera de su jurisdicción: " + string.Join(", ", fuera) + ".");
        }
    }

    /// <summary>
    /// Si un proyecto con esos municipios está dentro de lo que el actor puede ver. Según el Cuadro 3 del SRS, la
    /// autoridad local ve la información de su jurisdicción, la regional la de los municipios de su región y la nacional
    /// todo el territorio. Un proyecto que toca el territorio de la autoridad (aunque abarque otros) es de su
    /// jurisdicción. Sin municipios conocidos o sin territorio, una autoridad que no es nacional no lo ve: ante la duda se
    /// muestra de menos, no de más. Los demás roles (el auditor) no tienen restricción territorial.
    /// </summary>
    public static bool Cubre(Actor actor, IReadOnlyList<string>? codigosMunicipio)
    {
        if (actor.Rol != Rol.Autoridad || actor.Ambito == Ambito.Nacional)
        {
            return true;
        }
        if (codigosMunicipio is null || codigosMunicipio.Count == 0 || string.IsNullOrWhiteSpace(actor.Territorio))
        {
            return false;
        }
        return actor.Ambito switch
        {
            Ambito.Local => codigosMunicipio.Contains(actor.Territorio),
            Ambito.Regional => codigosMunicipio.Any(m => DepartamentoDe(m) == actor.Territorio),
            _ => false,
        };
    }
}
