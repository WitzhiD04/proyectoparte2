using System.Text.Json.Nodes;
using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;

namespace ConsultaDanos.Infraestructura;

/// <summary>
/// Formato de las respuestas. El mapa es un GeoJSON FeatureCollection (RF-ED-11): una "zona" por municipio (coropleta,
/// con el índice de daño y la distribución por nivel en <c>properties</c>) y, si se pidieron, un "activo" por punto.
/// Toda respuesta lleva <c>datoAl</c> y <c>degradado</c> en la raíz (AD-16, CON-6).
/// </summary>
public sealed class FormatoJson : IFormatoDanos
{
    public string Mapa(FiltroMapa filtro, IReadOnlyList<ZonaMapa> zonas, IReadOnlyDictionary<string, string> geometrias,
        IReadOnlyList<PuntoMapa> puntos, DateTimeOffset? datoAl)
    {
        JsonArray features = [];
        Distribucion total = new();
        foreach (ZonaMapa z in zonas)
        {
            total.Sumar(z.Distribucion);
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = geometrias.TryGetValue(z.Codigo, out string? g) ? JsonNode.Parse(g) : null,
                ["properties"] = new JsonObject
                {
                    ["tipo"] = "zona",
                    ["codigoMunicipio"] = z.Codigo,
                    ["nombre"] = z.Nombre,
                    ["codigoDepartamento"] = z.CodigoDepartamento,
                    ["activos"] = z.Distribucion.Total,
                    ["indiceDano"] = z.Distribucion.Indice,
                    ["distribucion"] = JsonDistribucion(z.Distribucion),
                    ["conConflicto"] = z.ConConflicto,
                },
            });
        }
        foreach (PuntoMapa p in puntos)
        {
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(p.Lon, p.Lat) },
                ["properties"] = new JsonObject
                {
                    ["tipo"] = "activo",
                    ["idActivo"] = p.IdActivo,
                    ["idEvaluacion"] = p.IdEvaluacion,
                    ["tipoActivo"] = p.TipoActivo,
                    ["nivelDano"] = p.Nivel.ToString(),
                    ["peso"] = IndiceDano.Peso(p.Nivel),
                    ["validada"] = p.Validacion == EstadoValidacion.VALIDADA,
                    ["estadoValidacion"] = p.Validacion?.ToString(),
                    ["conflictoPendiente"] = p.ConflictoPendiente,
                    ["codigoMunicipio"] = p.CodigoMunicipio,
                    ["datoAl"] = p.DatoAl,
                },
            });
        }
        return new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["idDesastre"] = filtro.Desastre,
            ["datoAl"] = datoAl,
            ["degradado"] = false,
            ["filtros"] = new JsonObject
            {
                ["nivelMinimo"] = filtro.NivelMinimo.ToString(), ["soloValidadas"] = filtro.SoloValidadas, ["puntos"] = filtro.Puntos,
            },
            ["resumen"] = new JsonObject
            {
                ["activos"] = total.Total, ["indiceDano"] = total.Indice, ["distribucion"] = JsonDistribucion(total),
                ["conConflicto"] = zonas.Sum(z => z.ConConflicto),
            },
            ["features"] = features,
        }.ToJsonString();
    }

    public string Consolidado(Consolidado c)
    {
        JsonArray zonas = [];
        foreach (ZonaPriorizada z in c.Zonas)
        {
            JsonArray porTipo = [];
            foreach (string tipo in z.Zona.Conteos.Select(x => x.TipoActivo).Distinct().Order(StringComparer.Ordinal))
            {
                Distribucion d = z.Zona.De(tipo);
                porTipo.Add(new JsonObject
                {
                    ["tipoActivo"] = tipo,
                    ["nombre"] = z.Zona.Conteos.First(x => x.TipoActivo == tipo).NombreTipo,
                    ["critico"] = c.TiposCriticos.Contains(tipo),
                    ["activos"] = d.Total,
                    ["indiceDano"] = d.Indice,
                    ["distribucion"] = JsonDistribucion(d),
                });
            }
            zonas.Add(new JsonObject
            {
                ["prioridad"] = z.Prioridad,
                ["codigoMunicipio"] = z.Zona.CodigoMunicipio,
                ["nombre"] = z.Zona.Nombre,
                ["codigoDepartamento"] = z.Zona.CodigoDepartamento,
                ["activos"] = z.Total.Total,
                ["indiceDano"] = z.Indice,
                ["criticosGraves"] = z.CriticosGraves,
                ["distribucion"] = JsonDistribucion(z.Total),
                ["porTipo"] = porTipo,
            });
        }
        JsonArray tipos = [];
        foreach (ResumenTipo t in c.PorTipo)
        {
            tipos.Add(new JsonObject
            {
                ["tipoActivo"] = t.TipoActivo, ["nombre"] = t.Nombre, ["critico"] = t.Critico, ["activos"] = t.Distribucion.Total,
                ["indiceDano"] = t.Distribucion.Indice, ["distribucion"] = JsonDistribucion(t.Distribucion),
            });
        }
        JsonArray integraciones = [];
        foreach (IntegracionLeida i in c.Integraciones)
        {
            integraciones.Add(new JsonObject
            {
                ["sistema"] = i.Sistema, ["estado"] = i.Estado, ["desde"] = i.Desde, ["mensajesEnCola"] = i.MensajesEnCola,
            });
        }
        return new JsonObject
        {
            ["idDesastre"] = c.IdDesastre,
            ["datoAl"] = c.DatoAl,
            ["degradado"] = false,
            ["soloValidadas"] = true,
            ["tiposCriticos"] = new JsonArray(c.TiposCriticos.Order(StringComparer.Ordinal).Select(t => (JsonNode?)t).ToArray()),
            ["total"] = new JsonObject
            {
                ["activos"] = c.Total.Total, ["indiceDano"] = c.Total.Indice, ["distribucion"] = JsonDistribucion(c.Total),
            },
            ["zonas"] = zonas,
            ["porTipo"] = tipos,
            ["integraciones"] = integraciones,
        }.ToJsonString();
    }

    public string MarcarDegradado(string respuesta)
    {
        JsonObject objeto = JsonNode.Parse(respuesta)!.AsObject();
        objeto["degradado"] = true;
        return objeto.ToJsonString();
    }

    private static JsonObject JsonDistribucion(Distribucion d)
    {
        JsonObject o = [];
        foreach (NivelDano n in Distribucion.Niveles)
        {
            o[n.ToString()] = d[n];
        }
        return o;
    }
}
