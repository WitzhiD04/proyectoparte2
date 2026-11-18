using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdaptadorSe6.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "se6");

            migrationBuilder.CreateTable(
                name: "bandeja_salida",
                schema: "se6",
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
                schema: "se6",
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
                name: "mensajes_procesados",
                schema: "se6",
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
                schema: "se6",
                table: "bandeja_salida",
                columns: new[] { "instante", "orden" },
                filter: "secuencia_productor IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_publicar",
                schema: "se6",
                table: "bandeja_salida",
                column: "secuencia_productor",
                unique: true,
                filter: "secuencia_productor IS NOT NULL AND publicado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bandeja_salida_clave_idempotencia",
                schema: "se6",
                table: "bandeja_salida",
                column: "clave_idempotencia",
                unique: true);

            // Permisos de mínimo privilegio del rol del adaptador (DT-02). El rol lo crea el migrador antes de aplicar
            // esta migración. Solo ve su esquema (la base "adaptadores" la comparten los adaptadores, DA-11), no puede
            // crear objetos ni borrar filas. El registro de mensajes procesados es de solo inserción; la bandeja admite
            // UPDATE porque el relevo numera y marca como publicado (DA-04).
            migrationBuilder.Sql(@"
                REVOKE ALL ON SCHEMA se6 FROM PUBLIC;
                GRANT USAGE ON SCHEMA se6 TO adaptador_se6_app;
                GRANT SELECT, INSERT ON se6.mensajes_procesados TO adaptador_se6_app;
                GRANT SELECT, INSERT, UPDATE ON se6.bandeja_salida, se6.contador_productor TO adaptador_se6_app;
                DO $$ BEGIN
                    EXECUTE format('ALTER ROLE adaptador_se6_app IN DATABASE %I SET search_path = se6', current_database());
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_salida",
                schema: "se6");

            migrationBuilder.DropTable(
                name: "contador_productor",
                schema: "se6");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "se6");
        }
    }
}
