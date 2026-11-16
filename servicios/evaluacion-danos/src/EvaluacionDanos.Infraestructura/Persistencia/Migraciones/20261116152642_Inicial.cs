using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EvaluacionDanos.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

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
                name: "capa_parte",
                columns: table => new
                {
                    capa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_capa = table.Column<int>(type: "integer", nullable: false),
                    parte = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    recibida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capa_parte", x => new { x.capa, x.version_capa, x.parte });
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
                name: "desastre",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    registrado = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_desastre = table.Column<int>(type: "integer", nullable: false),
                    codigos_municipio = table.Column<List<string>>(type: "text[]", nullable: false),
                    zona_version = table.Column<int>(type: "integer", nullable: false),
                    organismo_responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    organismos = table.Column<List<string>>(type: "text[]", nullable: false),
                    version_organismos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_desastre", x => x.id);
                    table.CheckConstraint("ck_desastre_estado", "estado IN ('DETECTADO', 'REGISTRADO', 'EN_EVALUACION', 'EN_ATENCION', 'EN_RECUPERACION', 'EN_RECONSTRUCCION', 'CERRADO')");
                });

            migrationBuilder.CreateTable(
                name: "division_administrativa",
                columns: table => new
                {
                    capa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    codigo_padre = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    version_capa = table.Column<int>(type: "integer", nullable: false),
                    geometria = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_division_administrativa", x => new { x.capa, x.codigo });
                });

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
                name: "operacion_procesada",
                columns: table => new
                {
                    id_operacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_dispositivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resultado_json = table.Column<string>(type: "jsonb", nullable: false),
                    instante = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operacion_procesada", x => x.id_operacion);
                });

            migrationBuilder.CreateTable(
                name: "tipo_activo",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tipo_activo", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "activo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_activo_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    referencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    creado_por_dispositivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    codigo_municipio = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    version_capa = table.Column<int>(type: "integer", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fusionado_en = table.Column<Guid>(type: "uuid", nullable: true),
                    ubicacion = table.Column<Point>(type: "geometry(Point,4326)", nullable: true, computedColumnSql: "ST_SetSRID(ST_MakePoint(lon, lat), 4326)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activo", x => x.id);
                    table.CheckConstraint("ck_activo_codigo_municipio", "codigo_municipio ~ '^[0-9]{5}$'");
                    table.ForeignKey(
                        name: "FK_activo_activo_fusionado_en",
                        column: x => x.fusionado_en,
                        principalTable: "activo",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_activo_tipo_activo_tipo_activo_codigo",
                        column: x => x.tipo_activo_codigo,
                        principalTable: "tipo_activo",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "criterio_nivel",
                columns: table => new
                {
                    tipo_activo_codigo = table.Column<string>(type: "character varying(40)", nullable: false),
                    nivel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criterio = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_criterio_nivel", x => new { x.tipo_activo_codigo, x.nivel });
                    table.CheckConstraint("ck_criterio_nivel_nivel", "nivel IN ('SIN_DANO', 'LEVE', 'MODERADO', 'GRAVE', 'DESTRUCCION_TOTAL')");
                    table.ForeignKey(
                        name: "FK_criterio_nivel_tipo_activo_tipo_activo_codigo",
                        column: x => x.tipo_activo_codigo,
                        principalTable: "tipo_activo",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activo_version",
                columns: table => new
                {
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    id_dispositivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instante = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activo_version", x => new { x.activo_id, x.numero });
                    table.ForeignKey(
                        name: "FK_activo_version_activo_activo_id",
                        column: x => x.activo_id,
                        principalTable: "activo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    desastre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instante_campo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    evaluador = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    organizacion = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    tipo_activo_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nivel_dano = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    precision_m = table.Column<double>(type: "double precision", nullable: false),
                    instante_servidor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    id_dispositivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    desfase_reloj_ms = table.Column<long>(type: "bigint", nullable: false),
                    validado_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    instante_validacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duplicado_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluacion", x => x.id);
                    table.CheckConstraint("ck_evaluacion_estado", "estado IN ('PRELIMINAR', 'REGISTRADA', 'EN_CONFLICTO', 'VALIDADA', 'RECHAZADA', 'DUPLICADO')");
                    table.CheckConstraint("ck_evaluacion_nivel_dano", "nivel_dano IN ('SIN_DANO', 'LEVE', 'MODERADO', 'GRAVE', 'DESTRUCCION_TOTAL')");
                    table.ForeignKey(
                        name: "FK_evaluacion_activo_activo_id",
                        column: x => x.activo_id,
                        principalTable: "activo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evaluacion_desastre_desastre_id",
                        column: x => x.desastre_id,
                        principalTable: "desastre",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_evaluacion_evaluacion_duplicado_de",
                        column: x => x.duplicado_de,
                        principalTable: "evaluacion",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_evaluacion_tipo_activo_tipo_activo_codigo",
                        column: x => x.tipo_activo_codigo,
                        principalTable: "tipo_activo",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluacion_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    contenido_json = table.Column<string>(type: "jsonb", nullable: false),
                    autor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    id_dispositivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_activo_base = table.Column<int>(type: "integer", nullable: true),
                    en_conflicto = table.Column<bool>(type: "boolean", nullable: false),
                    instante = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detalle = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluacion_version", x => x.id);
                    table.CheckConstraint("ck_evaluacion_version_motivo", "motivo IN ('SINCRONIZACION', 'ACTUALIZACION', 'ASOCIACION', 'VALIDACION', 'RESOLUCION')");
                    table.ForeignKey(
                        name: "FK_evaluacion_version_evaluacion_evaluacion_id",
                        column: x => x.evaluacion_id,
                        principalTable: "evaluacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    instante_captura = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sha256_declarado = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    subida = table.Column<bool>(type: "boolean", nullable: false),
                    clave_objeto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    instante_subida = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidencia", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidencia_evaluacion_evaluacion_id",
                        column: x => x.evaluacion_id,
                        principalTable: "evaluacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conflicto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evaluacion_vigente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluacion_entrante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_entrante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regla = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    instante = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado_previo_entrante = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    decidido_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    justificacion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    instante_resolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conflicto", x => x.id);
                    table.CheckConstraint("ck_conflicto_decision", "decision IS NULL OR decision IN ('PREVALECE_ENTRANTE', 'PREVALECE_VIGENTE', 'FUSIONAR', 'RECHAZAR_DUPLICADO')");
                    table.CheckConstraint("ck_conflicto_estado", "estado IN ('PENDIENTE', 'RESUELTO')");
                    table.CheckConstraint("ck_conflicto_tipo", "tipo IN ('VERSION', 'DUPLICADO')");
                    table.ForeignKey(
                        name: "FK_conflicto_activo_activo_id",
                        column: x => x.activo_id,
                        principalTable: "activo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conflicto_evaluacion_evaluacion_entrante_id",
                        column: x => x.evaluacion_entrante_id,
                        principalTable: "evaluacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conflicto_evaluacion_evaluacion_vigente_id",
                        column: x => x.evaluacion_vigente_id,
                        principalTable: "evaluacion",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_conflicto_evaluacion_version_version_entrante_id",
                        column: x => x.version_entrante_id,
                        principalTable: "evaluacion_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidencia_vinculada",
                columns: table => new
                {
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conflicto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instante = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidencia_vinculada", x => new { x.evaluacion_id, x.evidencia_id });
                    table.ForeignKey(
                        name: "FK_evidencia_vinculada_conflicto_conflicto_id",
                        column: x => x.conflicto_id,
                        principalTable: "conflicto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evidencia_vinculada_evaluacion_evaluacion_id",
                        column: x => x.evaluacion_id,
                        principalTable: "evaluacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evidencia_vinculada_evidencia_evidencia_id",
                        column: x => x.evidencia_id,
                        principalTable: "evidencia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activo_codigo_municipio_actualizado_en",
                table: "activo",
                columns: new[] { "codigo_municipio", "actualizado_en" });

            migrationBuilder.CreateIndex(
                name: "IX_activo_fusionado_en",
                table: "activo",
                column: "fusionado_en");

            migrationBuilder.CreateIndex(
                name: "IX_activo_tipo_activo_codigo_lat_lon",
                table: "activo",
                columns: new[] { "tipo_activo_codigo", "lat", "lon" });

            migrationBuilder.CreateIndex(
                name: "IX_activo_ubicacion",
                table: "activo",
                column: "ubicacion")
                .Annotation("Npgsql:IndexMethod", "gist");

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
                name: "IX_conflicto_activo_id",
                table: "conflicto",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "IX_conflicto_estado",
                table: "conflicto",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_conflicto_evaluacion_entrante_id",
                table: "conflicto",
                column: "evaluacion_entrante_id");

            migrationBuilder.CreateIndex(
                name: "IX_conflicto_evaluacion_vigente_id",
                table: "conflicto",
                column: "evaluacion_vigente_id");

            migrationBuilder.CreateIndex(
                name: "IX_conflicto_version_entrante_id",
                table: "conflicto",
                column: "version_entrante_id");

            migrationBuilder.CreateIndex(
                name: "IX_division_administrativa_geometria",
                table: "division_administrativa",
                column: "geometria")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_evaluacion_activo_id",
                table: "evaluacion",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "IX_evaluacion_desastre_id",
                table: "evaluacion",
                column: "desastre_id");

            migrationBuilder.CreateIndex(
                name: "IX_evaluacion_duplicado_de",
                table: "evaluacion",
                column: "duplicado_de");

            migrationBuilder.CreateIndex(
                name: "IX_evaluacion_id_dispositivo",
                table: "evaluacion",
                column: "id_dispositivo");

            migrationBuilder.CreateIndex(
                name: "IX_evaluacion_tipo_activo_codigo",
                table: "evaluacion",
                column: "tipo_activo_codigo");

            migrationBuilder.CreateIndex(
                name: "IX_evaluacion_version_evaluacion_id_numero_version",
                table: "evaluacion_version",
                columns: new[] { "evaluacion_id", "numero_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidencia_evaluacion_id",
                table: "evidencia",
                column: "evaluacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidencia_vinculada_conflicto_id",
                table: "evidencia_vinculada",
                column: "conflicto_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidencia_vinculada_evidencia_id",
                table: "evidencia_vinculada",
                column: "evidencia_id");

            // Permisos de mínimo privilegio del rol de la aplicación (DT-02, AD-01, corrige H-14). El rol lo crea el
            // migrador antes de aplicar esta migración; las migraciones corren con el rol dueño (DT-19).
            // Los historiales son de solo inserción y lo garantiza la base, no el código (AD-30, AD-41, QA-13):
            // el servicio NO puede modificar ni borrar una versión de una evaluación, una versión de un activo, el
            // resultado guardado de una operación ni el vínculo de una evidencia. Tampoco puede borrar nada ni
            // cambiar el esquema.
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT ON evaluacion_version, activo_version, operacion_procesada, evidencia_vinculada TO evaluacion_danos_app;
                GRANT SELECT, INSERT, UPDATE ON activo, evaluacion, evidencia, conflicto TO evaluacion_danos_app;
                -- Catálogo en ejecución (AD-28) y copias locales alimentadas por eventos (E-29, DA-07).
                GRANT SELECT, INSERT, UPDATE ON tipo_activo, criterio_nivel, desastre, division_administrativa TO evaluacion_danos_app;
                GRANT SELECT, INSERT ON capa_parte TO evaluacion_danos_app;
                -- Bandeja de salida y contador del productor: el relevo numera y marca como publicado (DA-04).
                GRANT SELECT, INSERT, UPDATE ON bandeja_salida, contador_productor TO evaluacion_danos_app;
                -- Registro de eventos ya consumidos (consumo idempotente, AD-02).
                GRANT SELECT, INSERT ON mensajes_procesados TO evaluacion_danos_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activo_version");

            migrationBuilder.DropTable(
                name: "bandeja_salida");

            migrationBuilder.DropTable(
                name: "capa_parte");

            migrationBuilder.DropTable(
                name: "contador_productor");

            migrationBuilder.DropTable(
                name: "criterio_nivel");

            migrationBuilder.DropTable(
                name: "division_administrativa");

            migrationBuilder.DropTable(
                name: "evidencia_vinculada");

            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "operacion_procesada");

            migrationBuilder.DropTable(
                name: "conflicto");

            migrationBuilder.DropTable(
                name: "evidencia");

            migrationBuilder.DropTable(
                name: "evaluacion_version");

            migrationBuilder.DropTable(
                name: "evaluacion");

            migrationBuilder.DropTable(
                name: "activo");

            migrationBuilder.DropTable(
                name: "desastre");

            migrationBuilder.DropTable(
                name: "tipo_activo");
        }
    }
}
