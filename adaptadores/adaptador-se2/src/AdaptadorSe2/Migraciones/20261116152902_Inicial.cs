using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdaptadorSe2.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bandeja_salida",
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
                name: "capas_publicadas",
                columns: table => new
                {
                    capa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_capa = table.Column<int>(type: "integer", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    partes = table.Column<int>(type: "integer", nullable: false),
                    divisiones = table.Column<int>(type: "integer", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capas_publicadas", x => new { x.capa, x.version_capa });
                });

            migrationBuilder.CreateTable(
                name: "contador_productor",
                columns: table => new
                {
                    productor = table.Column<string>(type: "text", nullable: false),
                    ultimo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contador_productor", x => x.productor);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_numerar",
                table: "bandeja_salida",
                columns: new[] { "instante", "orden" },
                filter: "secuencia_productor IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_publicar",
                table: "bandeja_salida",
                column: "secuencia_productor",
                unique: true,
                filter: "secuencia_productor IS NOT NULL AND publicado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bandeja_salida_clave_idempotencia",
                table: "bandeja_salida",
                column: "clave_idempotencia",
                unique: true);

            // Permisos de mínimo privilegio del rol del adaptador (DT-02). El rol lo crea el migrador antes de aplicar
            // esta migración. El registro de capas publicadas es de solo inserción; la bandeja admite UPDATE porque el
            // relevo numera y marca como publicado (DA-04). No puede borrar nada ni cambiar el esquema.
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT ON capas_publicadas TO adaptador_se2_app;
                GRANT SELECT, INSERT, UPDATE ON bandeja_salida, contador_productor TO adaptador_se2_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_salida");

            migrationBuilder.DropTable(
                name: "capas_publicadas");

            migrationBuilder.DropTable(
                name: "contador_productor");
        }
    }
}
