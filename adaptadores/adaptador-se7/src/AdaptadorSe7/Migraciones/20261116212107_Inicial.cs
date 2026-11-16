using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdaptadorSe7.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "se7");

            migrationBuilder.CreateTable(
                name: "bandeja_salida",
                schema: "se7",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    secuencia = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    secuencia_productor = table.Column<long>(type: "bigint", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    clave_ruteo = table.Column<string>(type: "text", nullable: false),
                    clave_idempotencia = table.Column<string>(type: "text", nullable: false),
                    id_correlacion = table.Column<string>(type: "text", nullable: true),
                    sobre = table.Column<string>(type: "jsonb", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bandeja_salida", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contador_productor",
                schema: "se7",
                columns: table => new
                {
                    productor = table.Column<string>(type: "text", nullable: false),
                    ultimo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contador_productor", x => x.productor);
                });

            migrationBuilder.CreateTable(
                name: "marcas_avance",
                schema: "se7",
                columns: table => new
                {
                    flujo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cambios = table.Column<long>(type: "bigint", nullable: false),
                    actualizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marcas_avance", x => x.flujo);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "se7",
                columns: table => new
                {
                    clave_idempotencia = table.Column<string>(type: "text", nullable: false),
                    id_evento = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensajes_procesados", x => x.clave_idempotencia);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_numerar",
                schema: "se7",
                table: "bandeja_salida",
                columns: new[] { "instante", "orden" },
                filter: "secuencia_productor IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_publicar",
                schema: "se7",
                table: "bandeja_salida",
                column: "secuencia_productor",
                unique: true,
                filter: "secuencia_productor IS NOT NULL AND publicado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bandeja_salida_clave_idempotencia",
                schema: "se7",
                table: "bandeja_salida",
                column: "clave_idempotencia",
                unique: true);

            // Permisos de mínimo privilegio del rol del adaptador (DT-02). El rol lo crea el migrador antes de aplicar
            // esta migración. Solo ve su esquema (la base "adaptadores" la comparten los adaptadores, DA-11), no puede
            // crear objetos ni borrar filas. El registro de mensajes procesados es de solo inserción; la bandeja admite
            // UPDATE porque el relevo numera y marca como publicado (DA-04); la marca de avance se actualiza (AD-14).
            // La ruta de búsqueda del rol apunta a se7: la mensajería de comun usa SQL sin esquema.
            migrationBuilder.Sql(@"
                REVOKE ALL ON SCHEMA se7 FROM PUBLIC;
                GRANT USAGE ON SCHEMA se7 TO adaptador_se7_app;
                GRANT SELECT, INSERT ON se7.mensajes_procesados TO adaptador_se7_app;
                GRANT SELECT, INSERT, UPDATE ON se7.bandeja_salida, se7.contador_productor, se7.marcas_avance TO adaptador_se7_app;
                DO $$ BEGIN
                    EXECUTE format('ALTER ROLE adaptador_se7_app IN DATABASE %I SET search_path = se7', current_database());
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_salida",
                schema: "se7");

            migrationBuilder.DropTable(
                name: "contador_productor",
                schema: "se7");

            migrationBuilder.DropTable(
                name: "marcas_avance",
                schema: "se7");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "se7");
        }
    }
}
