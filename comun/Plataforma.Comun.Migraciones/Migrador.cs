using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Plataforma.Comun.Migraciones;

/// <summary>Un rol de PostgreSQL que el servicio usa para conectarse. La clave llega del entorno, nunca del código.</summary>
public sealed record RolBase(string Nombre, string Clave, bool Replicacion = false);

/// <summary>Una base del servicio: su DbContext construido con la cadena del dueño y, opcionalmente, su siembra.</summary>
public sealed record BaseDelServicio(string Nombre, Func<DbContext> CrearContexto, Func<string>? SqlSiembra = null);

/// <summary>Pasos del despliegue de esquema de un servicio (DT-19).</summary>
public static class Migrador
{
    /// <summary>
    /// 1. Crea o actualiza los roles (idempotente). 2. Aplica las migraciones de cada base (EF crea la base si no
    /// existe). 3. Si <paramref name="sembrar"/>, ejecuta la siembra de cada base que la tenga.
    /// </summary>
    public static async Task EjecutarAsync(string cadenaAdministracion, IReadOnlyList<RolBase> roles,
        IReadOnlyList<BaseDelServicio> bases, bool sembrar, ILogger log, CancellationToken ct = default)
    {
        await AsegurarRolesAsync(cadenaAdministracion, roles, log, ct);
        foreach (BaseDelServicio baseDatos in bases)
        {
            await using DbContext contexto = baseDatos.CrearContexto();
            IEnumerable<string> pendientes = await contexto.Database.GetPendingMigrationsAsync(ct);
            log.LogInformation("Base {Base}: migraciones pendientes {Pendientes}.", baseDatos.Nombre, string.Join(", ", pendientes));
            await contexto.Database.MigrateAsync(ct);
            if (sembrar && baseDatos.SqlSiembra is not null)
            {
                log.LogInformation("Base {Base}: cargando datos de prueba.", baseDatos.Nombre);
                await contexto.Database.ExecuteSqlRawAsync(baseDatos.SqlSiembra(), ct);
            }
        }
        log.LogInformation("Migración terminada.");
    }

    private static async Task AsegurarRolesAsync(string cadena, IReadOnlyList<RolBase> roles, ILogger log, CancellationToken ct)
    {
        await using NpgsqlConnection conexion = new(cadena);
        await conexion.OpenAsync(ct);
        foreach (RolBase rol in roles)
        {
            if (string.IsNullOrWhiteSpace(rol.Clave))
            {
                throw new InvalidOperationException($"Falta la clave del rol {rol.Nombre}.");
            }
            string atributos = rol.Replicacion ? "LOGIN REPLICATION" : "LOGIN";
            string nombre = new NpgsqlCommandBuilder().QuoteIdentifier(rol.Nombre);
            // CREATE ROLE no admite parámetros: la clave se escapa como literal.
            string clave = "'" + rol.Clave.Replace("'", "''") + "'";
            string sql = $@"DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = {Literal(rol.Nombre)}) THEN
                    CREATE ROLE {nombre} {atributos} PASSWORD {clave};
                ELSE
                    ALTER ROLE {nombre} {atributos} PASSWORD {clave};
                END IF;
            END $$;";
            await using NpgsqlCommand comando = new(sql, conexion);
            await comando.ExecuteNonQueryAsync(ct);
            log.LogInformation("Rol {Rol} listo.", rol.Nombre);
        }
    }

    private static string Literal(string texto) => "'" + texto.Replace("'", "''") + "'";
}
