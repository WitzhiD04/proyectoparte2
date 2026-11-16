using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;

namespace AdaptadorSe2;

/// <summary>Un municipio de la capa, como lo entrega SE-2.</summary>
public sealed record MunicipioLeido(string Codigo, string Nombre, string CodigoDepartamento, JsonElement Geometria);

/// <summary>
/// Una capa leída. La versión sale del contenido: el mismo archivo da siempre la misma versión, así que republicarlo
/// no produce eventos nuevos (las claves de idempotencia coinciden), aunque el adaptador pierda su registro.
/// </summary>
public sealed record CapaLeida(string Hash, int Version, string Fuente, IReadOnlyList<MunicipioLeido> Municipios);

/// <summary>
/// Lectura de la capa de municipios (GeoJSON del Marco Geoestadístico Nacional, ver datos/geo/LEEME.md) y su
/// traducción a EV-21 (E-11). Funciones puras: no tocan la base ni el bus.
/// </summary>
public static class Capas
{
    public const string SistemaExterno = "SE-2";
    public const string FuentePorOmision = "DANE MGN";

    public static CapaLeida Leer(byte[] contenido)
    {
        byte[] hash = SHA256.HashData(contenido);
        using JsonDocument documento = JsonDocument.Parse(contenido);
        JsonElement raiz = documento.RootElement;
        string fuente = raiz.TryGetProperty("fuente", out JsonElement f) && f.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(f.GetString()) ? f.GetString()! : FuentePorOmision;

        List<MunicipioLeido> municipios = [];
        foreach (JsonElement rasgo in raiz.GetProperty("features").EnumerateArray())
        {
            JsonElement propiedades = rasgo.GetProperty("properties");
            string codigo = Texto(propiedades, "codigo");
            string departamento = Texto(propiedades, "codigoDepartamento");
            JsonElement geometria = rasgo.GetProperty("geometry");
            string tipo = geometria.GetProperty("type").GetString() ?? "";
            // Se rechaza aquí lo que el contrato no admite, con un mensaje que dice cuál municipio está mal.
            if (codigo.Length != 5 || !codigo.All(char.IsAsciiDigit) || departamento.Length != 2
                || !codigo.StartsWith(departamento, StringComparison.Ordinal) || tipo is not ("Polygon" or "MultiPolygon"))
            {
                throw new InvalidDataException($"El municipio '{codigo}' de la capa no es válido (código, departamento o geometría).");
            }
            municipios.Add(new MunicipioLeido(codigo, Texto(propiedades, "nombre"), departamento, geometria.Clone()));
        }
        if (municipios.Count == 0)
        {
            throw new InvalidDataException("La capa no trae municipios.");
        }
        return new CapaLeida(Convert.ToHexStringLower(hash), VersionDe(hash), fuente, municipios);
    }

    /// <summary>
    /// El contrato pide una versión entera positiva: los primeros 31 bits del SHA-256 del archivo. No es un
    /// consecutivo: dos versiones no se ordenan por su número, solo se distinguen.
    /// </summary>
    public static int VersionDe(byte[] hash)
    {
        int version = (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) & 0x7FFFFFFF);
        return version == 0 ? 1 : version;
    }

    public static string Clave(int version, int parte) => $"capa:{CapaGeografica.MUNICIPIOS}:v{version}:{parte}";

    /// <summary>
    /// Un EV-21 por departamento, en orden de código (parte 1 de N, ..., N de N). La capa completa pesa cerca de
    /// medio MB y cada parte menos de 60 KB: cabe con holgura en un mensaje del bus (E-11).
    /// </summary>
    public static IReadOnlyList<Sobre> ASobres(CapaLeida capa, DateTimeOffset instante)
    {
        List<IGrouping<string, MunicipioLeido>> departamentos = capa.Municipios
            .GroupBy(m => m.CodigoDepartamento).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        List<Sobre> sobres = [];
        for (int i = 0; i < departamentos.Count; i++)
        {
            int parte = i + 1;
            List<Division> divisiones = departamentos[i].OrderBy(m => m.Codigo, StringComparer.Ordinal)
                .Select(m => new Division(m.Codigo, m.Nombre, m.CodigoDepartamento, m.Geometria)).ToList();
            CapaGeograficaActualizadaV1 datos = new(CapaGeografica.MUNICIPIOS, capa.Version, divisiones, capa.Fuente,
                parte, departamentos.Count);
            // Procedencia (AD-11): el dato viene de un sistema externo, no se origina en la plataforma.
            sobres.Add(Sobre.Crear(CatalogoEventos.CapaGeograficaActualizada, CatalogoEventos.AdaptadorSe2,
                Clave(capa.Version, parte), datos, instante, Procedencia.DeSistemaExterno(SistemaExterno),
                idCorrelacion: $"capa:{CapaGeografica.MUNICIPIOS}:v{capa.Version}"));
        }
        return sobres;
    }

    private static string Texto(JsonElement objeto, string nombre) =>
        objeto.TryGetProperty(nombre, out JsonElement valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()!.Trim()
            : "";
}
