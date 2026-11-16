using Microsoft.EntityFrameworkCore;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace AdaptadorSe2;

public sealed class OpcionesCapa
{
    /// <summary>Archivo GeoJSON de la capa de municipios. Relativo a la carpeta de la aplicación o absoluto.</summary>
    public string Ruta { get; set; } = "datos/geo/municipios-colombia.geojson";

    /// <summary>Cada cuánto se revisa si el archivo cambió (una versión nueva de la capa).</summary>
    public int IntervaloRevisionSegundos { get; set; } = 300;
}

public sealed record ResultadoPublicacionCapa(int Version, bool Publicada, int Partes);

/// <summary>
/// Publica la capa de municipios si su versión aún no salió (DA-07, E-11). El registro de la capa y sus EV-21 se
/// guardan en una sola transacción (AD-45): o quedan todas las partes en la bandeja o ninguna. El relevo de la bandeja
/// las entrega al bus con garantía; si el bus no está, esperan allí.
/// </summary>
public sealed class PublicacionDeCapas(AdaptadorSe2DbContext db, EscritorBandeja<AdaptadorSe2DbContext> bandeja)
{
    public async Task<ResultadoPublicacionCapa> PublicarSiEsNuevaAsync(byte[] contenido, CancellationToken ct)
    {
        CapaLeida capa = Capas.Leer(contenido);
        string nombre = CapaGeografica.MUNICIPIOS.ToString();
        if (await db.CapasPublicadas.AnyAsync(c => c.Capa == nombre && c.VersionCapa == capa.Version, ct))
        {
            return new ResultadoPublicacionCapa(capa.Version, false, 0);
        }

        DateTimeOffset ahora = DateTimeOffset.UtcNow;
        IReadOnlyList<Sobre> sobres = Capas.ASobres(capa, ahora);
        foreach (Sobre sobre in sobres)
        {
            bandeja.Agregar(sobre);   // valida contra el esquema de EV-21: una parte inválida no sale
        }
        db.CapasPublicadas.Add(new CapaPublicada
        {
            Capa = nombre, VersionCapa = capa.Version, Hash = capa.Hash, Partes = sobres.Count,
            Divisiones = capa.Municipios.Count, Instante = ahora,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Otra réplica del adaptador la publicó al mismo tiempo: la llave del registro y las claves de
            // idempotencia de la bandeja son únicas, así que esta transacción no dejó nada.
            db.ChangeTracker.Clear();
            return new ResultadoPublicacionCapa(capa.Version, false, 0);
        }
        return new ResultadoPublicacionCapa(capa.Version, true, sobres.Count);
    }
}

/// <summary>Revisa el archivo de la capa al arrancar y cada cierto tiempo; si es una versión nueva, la publica.</summary>
public sealed class VigilanteDeCapas(IServiceScopeFactory ambitos, OpcionesCapa opciones, IHostEnvironment entorno,
    ILogger<VigilanteDeCapas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string ruta = Path.IsPathRooted(opciones.Ruta) ? opciones.Ruta : Path.Combine(entorno.ContentRootPath, opciones.Ruta);
        TimeSpan espera = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                byte[] contenido = await File.ReadAllBytesAsync(ruta, ct);
                using IServiceScope ambito = ambitos.CreateScope();
                ResultadoPublicacionCapa resultado = await ambito.ServiceProvider.GetRequiredService<PublicacionDeCapas>()
                    .PublicarSiEsNuevaAsync(contenido, ct);
                if (resultado.Publicada)
                {
                    log.LogInformation("Capa de municipios v{Version}: {Partes} partes de EV-21 en la bandeja.",
                        resultado.Version, resultado.Partes);
                }
                espera = TimeSpan.FromSeconds(Math.Max(5, opciones.IntervaloRevisionSegundos));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // La base aún no responde o el archivo no está: se reintenta pronto, sin tumbar el proceso.
                log.LogWarning(e, "No se pudo revisar la capa {Ruta}; se reintenta.", ruta);
                espera = TimeSpan.FromSeconds(5);
            }
            await Task.Delay(espera, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
