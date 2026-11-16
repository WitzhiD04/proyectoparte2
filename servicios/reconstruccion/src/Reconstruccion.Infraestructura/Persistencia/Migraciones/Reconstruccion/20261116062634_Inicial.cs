using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones.Reconstruccion
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

            migrationBuilder.CreateTable(
                name: "desastres_locales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_desastres_locales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lotes_creacion",
                columns: table => new
                {
                    clave_idempotencia = table.Column<string>(type: "text", nullable: false),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    autor = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ids_proyectos = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lotes_creacion", x => x.clave_idempotencia);
                });

            migrationBuilder.CreateTable(
                name: "proyectos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    id_activo = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    alcance = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    presupuesto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    fuente_financiacion = table.Column<string>(type: "text", nullable: true),
                    responsable = table.Column<string>(type: "text", nullable: true),
                    contratista = table.Column<string>(type: "text", nullable: true),
                    inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    fin = table.Column<DateOnly>(type: "date", nullable: true),
                    avance = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    costo_ejecutado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proyectos", x => x.id);
                    table.CheckConstraint("ck_proyectos_alcance", "alcance IN ('Municipal', 'Regional')");
                    table.CheckConstraint("ck_proyectos_avance", "avance BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_proyectos_costo", "costo_ejecutado >= 0");
                    table.CheckConstraint("ck_proyectos_cronograma", "fin IS NULL OR inicio IS NULL OR fin > inicio");
                    table.CheckConstraint("ck_proyectos_estado", "estado IN ('Formulado', 'Aprobado', 'EnEjecucion', 'Finalizado', 'Cancelado')");
                    table.CheckConstraint("ck_proyectos_presupuesto", "presupuesto >= 0");
                });

            migrationBuilder.CreateTable(
                name: "evaluaciones_validadas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    id_activo = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_activo = table.Column<string>(type: "text", nullable: false),
                    tipo_activo = table.Column<string>(type: "text", nullable: false),
                    nivel_dano = table.Column<string>(type: "text", nullable: false),
                    municipio = table.Column<string>(type: "text", nullable: false),
                    codigo_municipio = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluaciones_validadas", x => x.id);
                    table.CheckConstraint("ck_evaluaciones_validadas_nivel", "nivel_dano IN ('SinDano', 'Leve', 'Moderado', 'Grave', 'DestruccionTotal')");
                    table.ForeignKey(
                        name: "FK_evaluaciones_validadas_desastres_locales_id_desastre",
                        column: x => x.id_desastre,
                        principalTable: "desastres_locales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    ubicacion = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    autor = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos", x => x.id);
                    table.ForeignKey(
                        name: "FK_documentos_proyectos_id_proyecto",
                        column: x => x.id_proyecto,
                        principalTable: "proyectos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluaciones_origen",
                columns: table => new
                {
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    id_evaluacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_activo = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_activo = table.Column<string>(type: "text", nullable: false),
                    nivel_dano = table.Column<string>(type: "text", nullable: false),
                    codigo_municipio = table.Column<string>(type: "text", nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluaciones_origen", x => new { x.id_proyecto, x.id_evaluacion });
                    table.ForeignKey(
                        name: "FK_evaluaciones_origen_proyectos_id_proyecto",
                        column: x => x.id_proyecto,
                        principalTable: "proyectos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "historial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    tipo_cambio = table.Column<string>(type: "text", nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    autor = table.Column<string>(type: "text", nullable: false),
                    justificacion = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    procedencia = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial", x => x.id);
                    table.ForeignKey(
                        name: "FK_historial_proyectos_id_proyecto",
                        column: x => x.id_proyecto,
                        principalTable: "proyectos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "registros_avance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    porcentaje = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    autor = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    procedencia = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_avance", x => x.id);
                    table.CheckConstraint("ck_registros_avance_porcentaje", "porcentaje BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_registros_avance_proyectos_id_proyecto",
                        column: x => x.id_proyecto,
                        principalTable: "proyectos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "registros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    concepto = table.Column<string>(type: "text", nullable: false),
                    autor = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_costo", x => x.id);
                    table.CheckConstraint("ck_registros_costo_monto", "monto > 0");
                    table.ForeignKey(
                        name: "FK_registros_costo_proyectos_id_proyecto",
                        column: x => x.id_proyecto,
                        principalTable: "proyectos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "IX_documentos_id_proyecto",
                table: "documentos",
                column: "id_proyecto");

            migrationBuilder.CreateIndex(
                name: "ux_evaluacion_vigente",
                table: "evaluaciones_origen",
                column: "id_evaluacion",
                unique: true,
                filter: "vigente");

            migrationBuilder.CreateIndex(
                name: "ix_evaluaciones_desastre",
                table: "evaluaciones_validadas",
                column: "id_desastre");

            migrationBuilder.CreateIndex(
                name: "IX_historial_id_proyecto_version",
                table: "historial",
                columns: new[] { "id_proyecto", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_historial_proyecto_instante",
                table: "historial",
                columns: new[] { "id_proyecto", "instante" });

            migrationBuilder.CreateIndex(
                name: "ix_proyectos_desastre",
                table: "proyectos",
                column: "id_desastre");

            migrationBuilder.CreateIndex(
                name: "IX_registros_avance_id_proyecto",
                table: "registros_avance",
                column: "id_proyecto");

            migrationBuilder.CreateIndex(
                name: "IX_registros_costo_id_proyecto",
                table: "registros_costo",
                column: "id_proyecto");

            // Mínimo privilegio (DT-02). El servicio se conecta como reconstruccion_app, que no tiene permisos de DDL y
            // NO puede modificar ni borrar el historial, los avances, los costos ni los documentos: un error se corrige
            // con otra entrada (AD-41, QA-15). El rol lo crea el migrador antes de aplicar esta migración.
            migrationBuilder.Sql(@"
                GRANT SELECT ON desastres_locales, evaluaciones_validadas TO reconstruccion_app;
                GRANT SELECT, INSERT, UPDATE ON proyectos, evaluaciones_origen TO reconstruccion_app;
                GRANT SELECT, INSERT ON lotes_creacion TO reconstruccion_app;
                GRANT SELECT, INSERT ON historial, registros_avance, registros_costo, documentos TO reconstruccion_app;
                -- Bandeja de salida y contador del productor: el relevo numera y marca como publicado (DA-04).
                GRANT SELECT, INSERT, UPDATE ON bandeja_salida, contador_productor TO reconstruccion_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_salida");

            migrationBuilder.DropTable(
                name: "contador_productor");

            migrationBuilder.DropTable(
                name: "documentos");

            migrationBuilder.DropTable(
                name: "evaluaciones_origen");

            migrationBuilder.DropTable(
                name: "evaluaciones_validadas");

            migrationBuilder.DropTable(
                name: "historial");

            migrationBuilder.DropTable(
                name: "lotes_creacion");

            migrationBuilder.DropTable(
                name: "registros_avance");

            migrationBuilder.DropTable(
                name: "registros_costo");

            migrationBuilder.DropTable(
                name: "desastres_locales");

            migrationBuilder.DropTable(
                name: "proyectos");
        }
    }
}
