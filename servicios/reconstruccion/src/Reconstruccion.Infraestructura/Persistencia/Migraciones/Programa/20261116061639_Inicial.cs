using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones.Programa
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
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

            migrationBuilder.CreateTable(
                name: "proyeccion_proyectos",
                columns: table => new
                {
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    presupuesto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    costo_ejecutado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    avance = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proyeccion_proyectos", x => x.id_proyecto);
                });

            migrationBuilder.CreateIndex(
                name: "ix_proyeccion_desastre",
                table: "proyeccion_proyectos",
                column: "id_desastre");

            // Mínimo privilegio (DT-02): la Consulta del Programa solo lee y actualiza su proyección y registra los
            // eventos ya aplicados (consumo idempotente). No tiene permisos de DDL.
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT, UPDATE ON proyeccion_proyectos TO programa_app;
                GRANT SELECT, INSERT ON mensajes_procesados TO programa_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "proyeccion_proyectos");
        }
    }
}
