using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

public sealed record CriterioSolicitado(string? Nivel, string? Criterio);

/// <summary>Un tipo de activo por crear (todo obligatorio) o por modificar (solo lo que cambia).</summary>
public sealed record SolicitudTipoActivo(string? Codigo, string? Nombre, List<CriterioSolicitado>? Criterios);

/// <summary>Catálogo de tipos de activo con sus criterios por nivel de daño (RF-ED-04, RF-ED-05).</summary>
public sealed class ConsultarCatalogo(IConsultasEvaluaciones consultas) : ICasoDeUso<bool, List<TipoActivoDto>>
{
    public async Task<Resultado<List<TipoActivoDto>>> EjecutarAsync(bool _, CancellationToken cancelar = default) =>
        Resultado<List<TipoActivoDto>>.Ok(await consultas.TiposActivoAsync(cancelar));
}

/// <summary>
/// Administración del catálogo en ejecución (AD-28, DA-18, corrige H-17). Un tipo nuevo se puede usar en la siguiente
/// sincronización sin modificar ni redesplegar ningún componente (QA-10), porque el validador lee el catálogo del
/// almacén. Cada cambio publica EV-25 (E-16) para que las consultas conozcan el tipo.
/// </summary>
public sealed class CrearTipoActivo(IAlmacenEvaluaciones almacen, IPublicadorHechos hechos, IReloj reloj)
    : ICasoDeUso<SolicitudTipoActivo, TipoActivoDto>
{
    public async Task<Resultado<TipoActivoDto>> EjecutarAsync(SolicitudTipoActivo solicitud, CancellationToken cancelar = default)
    {
        if (!CatalogoTipos.CodigoValido(solicitud.Codigo))
        {
            return Resultado<TipoActivoDto>.Falla(TipoFallo.Invalido, "El código debe empezar por una letra minúscula y tener " +
                $"solo minúsculas, dígitos y guion bajo (máximo {Limites.CodigoTipo} caracteres)");
        }
        string? error = CatalogoTipos.ValidarNombre(solicitud.Nombre) ?? CatalogoTipos.ValidarCriterios(Pares(solicitud.Criterios));
        if (error != null)
        {
            return Resultado<TipoActivoDto>.Falla(TipoFallo.Invalido, error);
        }
        if (await almacen.TipoActivoAsync(solicitud.Codigo!, cancelar) != null)
        {
            return Resultado<TipoActivoDto>.Falla(TipoFallo.ReglaIncumplida, $"Ya existe el tipo de activo '{solicitud.Codigo}'");
        }

        var ahora = reloj.Ahora();
        var tipo = new TipoActivo { Codigo = solicitud.Codigo!, Nombre = solicitud.Nombre!.Trim(), Version = 1, ActualizadoEn = ahora };
        foreach (var c in solicitud.Criterios!)
        {
            tipo.Criterios.Add(new CriterioNivel
            {
                TipoActivoCodigo = tipo.Codigo, Nivel = Enum.Parse<NivelDano>(c.Nivel!), Criterio = c.Criterio!.Trim()
            });
        }
        almacen.Agregar(tipo);
        var dto = CatalogoDto.De(tipo);
        hechos.Registrar(new TipoActivoActualizado(tipo.Codigo, tipo.Nombre, dto.Criterios, tipo.Version, ahora));

        return await CambioEnServidor.GuardarAsync<TipoActivoDto>(almacen, cancelar) ?? Resultado<TipoActivoDto>.Ok(dto);
    }

    internal static List<(string?, string?)>? Pares(List<CriterioSolicitado>? criterios) =>
        criterios?.Select(c => (c.Nivel, c.Criterio)).ToList();
}

public sealed class ModificarTipoActivo(IAlmacenEvaluaciones almacen, IPublicadorHechos hechos, IReloj reloj)
    : ICasoDeUso<SolicitudTipoActivo, TipoActivoDto>
{
    public async Task<Resultado<TipoActivoDto>> EjecutarAsync(SolicitudTipoActivo solicitud, CancellationToken cancelar = default)
    {
        if (solicitud.Nombre is null && solicitud.Criterios is null)
        {
            return Resultado<TipoActivoDto>.Falla(TipoFallo.Invalido, "La modificación no trae ni nombre ni criterios");
        }
        string? error = (solicitud.Nombre is null ? null : CatalogoTipos.ValidarNombre(solicitud.Nombre))
            ?? (solicitud.Criterios is null ? null : CatalogoTipos.ValidarCriterios(CrearTipoActivo.Pares(solicitud.Criterios)));
        if (error != null)
        {
            return Resultado<TipoActivoDto>.Falla(TipoFallo.Invalido, error);
        }
        var tipo = solicitud.Codigo is null ? null : await almacen.TipoActivoAsync(solicitud.Codigo, cancelar);
        if (tipo is null)
        {
            return Resultado<TipoActivoDto>.Falla(TipoFallo.NoEncontrado, $"No existe el tipo de activo '{solicitud.Codigo}'");
        }

        var ahora = reloj.Ahora();
        tipo.Nombre = solicitud.Nombre?.Trim() ?? tipo.Nombre;
        foreach (var c in solicitud.Criterios ?? [])
        {
            // Las cinco categorías son fijas (RN-5): el criterio de cada una se reemplaza, no se agrega ni se quita.
            var nivel = Enum.Parse<NivelDano>(c.Nivel!);
            var criterio = tipo.Criterios.FirstOrDefault(x => x.Nivel == nivel);
            if (criterio is null)
            {
                tipo.Criterios.Add(new CriterioNivel { TipoActivoCodigo = tipo.Codigo, Nivel = nivel, Criterio = c.Criterio!.Trim() });
            }
            else
            {
                criterio.Criterio = c.Criterio!.Trim();
            }
        }
        tipo.Version++;   // token de concurrencia: dos administradores a la vez no se pisan
        tipo.ActualizadoEn = ahora;
        var dto = CatalogoDto.De(tipo);
        hechos.Registrar(new TipoActivoActualizado(tipo.Codigo, tipo.Nombre, dto.Criterios, tipo.Version, ahora));

        return await CambioEnServidor.GuardarAsync<TipoActivoDto>(almacen, cancelar) ?? Resultado<TipoActivoDto>.Ok(dto);
    }
}

public static class CatalogoDto
{
    public static TipoActivoDto De(TipoActivo tipo) => new(tipo.Codigo, tipo.Nombre,
        tipo.Criterios.OrderBy(c => c.Nivel).Select(c => new CriterioDto(c.Nivel, c.Criterio)).ToList(), tipo.Version);
}
