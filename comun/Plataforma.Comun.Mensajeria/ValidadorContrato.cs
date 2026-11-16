using System.Text.Json;
using Json.Schema;
using Plataforma.Comun.Eventos;

namespace Plataforma.Comun.Mensajeria;

/// <summary>Un evento que no cumple el contrato. Es un error permanente: reintentarlo no lo arregla.</summary>
public sealed class EventoInvalidoException : Exception
{
    public EventoInvalidoException(string mensaje) : base(mensaje) { }
    public EventoInvalidoException(string mensaje, Exception interna) : base(mensaje, interna) { }
}

/// <summary>
/// Valida un sobre y sus datos contra los esquemas JSON del contrato (DT-17).
/// El productor valida antes de escribir en la bandeja, de modo que un evento inválido nunca sale.
/// El consumidor valida al recibir y aparta a la cola de mensajes muertos lo que no cumple.
/// </summary>
public sealed class ValidadorContrato
{
    private readonly Dictionary<string, JsonSchema> _esquemas = new(StringComparer.Ordinal);
    private static readonly EvaluationOptions Evaluacion = new() { OutputFormat = OutputFormat.List, RequireFormatValidation = true };

    public static ValidadorContrato Instancia { get; } = new();

    private ValidadorContrato()
    {
        SchemaRegistry registro = new();
        BuildOptions opciones = new() { SchemaRegistry = registro };
        // El esquema común primero: los demás lo referencian.
        foreach (string nombre in EsquemasEmbebidos.Nombres.OrderBy(n => n == EsquemasEmbebidos.Comun ? 0 : 1))
        {
            JsonSchema esquema = JsonSchema.FromText(EsquemasEmbebidos.Leer(nombre), opciones, EsquemasEmbebidos.UriDe(nombre));
            registro.Register(esquema);
            _esquemas[nombre] = esquema;
        }
    }

    /// <summary>Lanza <see cref="EventoInvalidoException"/> si el sobre o sus datos no cumplen el contrato.</summary>
    public void Validar(Sobre sobre)
    {
        DefinicionEvento definicion;
        try
        {
            definicion = CatalogoEventos.PorTipo(sobre.Tipo);
        }
        catch (KeyNotFoundException e)
        {
            throw new EventoInvalidoException(e.Message, e);
        }
        if (definicion.Nombre != sobre.Nombre)
        {
            throw new EventoInvalidoException($"El evento {sobre.Tipo} se llama {definicion.Nombre}, no {sobre.Nombre}.");
        }
        if (!definicion.EnAlcance)
        {
            throw new EventoInvalidoException($"El evento {sobre.Tipo} no tiene esquema en esta entrega.");
        }

        JsonElement sobreJson = JsonSerializer.SerializeToElement(sobre, JsonContrato.Opciones);
        Exigir(EsquemasEmbebidos.Sobre, sobreJson, "sobre");

        string esquemaDatos = $"{sobre.Tipo}.v{sobre.Version}.json";
        if (!_esquemas.ContainsKey(esquemaDatos))
        {
            throw new EventoInvalidoException($"No existe el esquema {esquemaDatos}.");
        }
        Exigir(esquemaDatos, sobre.Datos, "datos");
    }

    /// <summary>
    /// Como <see cref="Validar"/>, pero un tipo o una versión que el contrato desplegado no conoce no es un error: se valida
    /// solo el sobre y devuelve falso (E-34). Un tipo conocido se valida completo, con su nombre y sus datos.
    /// </summary>
    public bool ValidarConocidos(Sobre sobre)
    {
        DefinicionEvento? definicion = CatalogoEventos.Todos.FirstOrDefault(e => e.Tipo == sobre.Tipo);
        if (definicion is null || !definicion.EnAlcance || !_esquemas.ContainsKey($"{sobre.Tipo}.v{sobre.Version}.json"))
        {
            Exigir(EsquemasEmbebidos.Sobre, JsonSerializer.SerializeToElement(sobre, JsonContrato.Opciones), "sobre");
            return false;
        }
        Validar(sobre);
        return true;
    }

    /// <summary>Valida un JSON cualquiera contra un esquema del contrato. Útil en las pruebas.</summary>
    public IReadOnlyList<string> Errores(string nombreEsquema, JsonElement instancia)
    {
        EvaluationResults resultado = _esquemas[nombreEsquema].Evaluate(instancia, Evaluacion);
        if (resultado.IsValid)
        {
            return [];
        }
        List<string> errores = new();
        IEnumerable<EvaluationResults> detalles = resultado.Details ?? [resultado];
        foreach (EvaluationResults detalle in detalles.Where(d => d.Errors is { Count: > 0 }))
        {
            foreach (KeyValuePair<string, string> error in detalle.Errors!)
            {
                errores.Add($"{detalle.InstanceLocation}: {error.Key} - {error.Value}");
            }
        }
        return errores.Count > 0 ? errores : ["no cumple el esquema"];
    }

    private void Exigir(string nombreEsquema, JsonElement instancia, string parte)
    {
        IReadOnlyList<string> errores = Errores(nombreEsquema, instancia);
        if (errores.Count > 0)
        {
            throw new EventoInvalidoException($"El {parte} no cumple {nombreEsquema}: {string.Join("; ", errores.Take(5))}");
        }
    }
}
