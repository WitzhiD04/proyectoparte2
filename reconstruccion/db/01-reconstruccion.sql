-- Base de datos del servicio de Reconstrucción (se ejecuta sobre la base "reconstruccion").
-- El servicio se conecta como reconstruccion_app, que NO puede modificar ni borrar el historial.

CREATE ROLE reconstruccion_app LOGIN PASSWORD 'reconstruccion_local';
-- Usuario con el que la réplica copia los datos de forma continua (ver 04-replicacion.sh).
CREATE ROLE replicador WITH REPLICATION LOGIN PASSWORD 'replicador_local';

-- Copias locales de datos de otros módulos -------------------------------------------------
CREATE TABLE desastres_locales (
    id      uuid PRIMARY KEY,
    nombre  text NOT NULL,
    estado  text NOT NULL
);

CREATE TABLE evaluaciones_validadas (
    id             uuid PRIMARY KEY,
    id_desastre    uuid NOT NULL REFERENCES desastres_locales (id),
    id_activo      uuid NOT NULL,
    nombre_activo  text NOT NULL,
    tipo_activo    text NOT NULL,
    nivel_dano     text NOT NULL CHECK (nivel_dano IN ('SinDano', 'Leve', 'Moderado', 'Grave', 'DestruccionTotal')),
    municipio      text NOT NULL,
    estado         text NOT NULL
);
CREATE INDEX ix_evaluaciones_desastre ON evaluaciones_validadas (id_desastre);

-- Proyectos --------------------------------------------------------------------------------
CREATE TABLE proyectos (
    id                   uuid PRIMARY KEY,
    id_desastre          uuid NOT NULL,
    id_activo            uuid NOT NULL,
    nombre               text NOT NULL,
    alcance              text NOT NULL CHECK (alcance IN ('Municipal', 'Regional')),
    estado               text NOT NULL CHECK (estado IN ('Formulado', 'Aprobado', 'EnEjecucion', 'Finalizado', 'Cancelado')),
    presupuesto          numeric(18, 2) CHECK (presupuesto >= 0),
    fuente_financiacion  text,
    responsable          text,
    contratista          text,
    inicio               date,
    fin                  date,
    avance               numeric(5, 2) NOT NULL DEFAULT 0 CHECK (avance BETWEEN 0 AND 100),
    costo_ejecutado      numeric(18, 2) NOT NULL DEFAULT 0 CHECK (costo_ejecutado >= 0),
    version              integer NOT NULL,
    CHECK (fin IS NULL OR inicio IS NULL OR fin > inicio)
);
CREATE INDEX ix_proyectos_desastre ON proyectos (id_desastre);

CREATE TABLE evaluaciones_origen (
    id_proyecto    uuid NOT NULL REFERENCES proyectos (id),
    id_evaluacion  uuid NOT NULL,
    id_activo      uuid NOT NULL,
    tipo_activo    text NOT NULL,
    nivel_dano     text NOT NULL,
    vigente        boolean NOT NULL DEFAULT true,
    PRIMARY KEY (id_proyecto, id_evaluacion)
);
-- Una evaluación está vinculada como máximo a un proyecto que no esté cancelado.
CREATE UNIQUE INDEX ux_evaluacion_vigente ON evaluaciones_origen (id_evaluacion) WHERE vigente;

-- Registros de solo inserción --------------------------------------------------------------
CREATE TABLE historial (
    id              uuid PRIMARY KEY,
    id_proyecto     uuid NOT NULL REFERENCES proyectos (id),
    version         integer NOT NULL,
    tipo_cambio     text NOT NULL,
    valor_anterior  text,
    valor_nuevo     text,
    autor           text NOT NULL,
    justificacion   text NOT NULL,
    instante        timestamptz NOT NULL,
    procedencia     text NOT NULL,
    UNIQUE (id_proyecto, version)
);
CREATE INDEX ix_historial_proyecto_instante ON historial (id_proyecto, instante);

CREATE TABLE registros_avance (
    id           uuid PRIMARY KEY,
    id_proyecto  uuid NOT NULL REFERENCES proyectos (id),
    porcentaje   numeric(5, 2) NOT NULL CHECK (porcentaje BETWEEN 0 AND 100),
    autor        text NOT NULL,
    instante     timestamptz NOT NULL,
    procedencia  text NOT NULL
);

CREATE TABLE registros_costo (
    id           uuid PRIMARY KEY,
    id_proyecto  uuid NOT NULL REFERENCES proyectos (id),
    monto        numeric(18, 2) NOT NULL CHECK (monto > 0),
    concepto     text NOT NULL,
    autor        text NOT NULL,
    instante     timestamptz NOT NULL
);

CREATE TABLE documentos (
    id           uuid PRIMARY KEY,
    id_proyecto  uuid NOT NULL REFERENCES proyectos (id),
    nombre       text NOT NULL,
    tipo         text NOT NULL,
    ubicacion    text NOT NULL,
    hash         text NOT NULL,
    autor        text NOT NULL,
    instante     timestamptz NOT NULL
);

-- Lotes y bandeja de salida ----------------------------------------------------------------
CREATE TABLE lotes_creacion (
    clave_idempotencia  text PRIMARY KEY,
    id_desastre         uuid NOT NULL,
    autor               text NOT NULL,
    instante            timestamptz NOT NULL,
    ids_proyectos       uuid[] NOT NULL
);

CREATE TABLE bandeja_salida (
    id                  uuid PRIMARY KEY,
    secuencia           bigint GENERATED ALWAYS AS IDENTITY,
    tipo                text NOT NULL,
    clave_idempotencia  text NOT NULL UNIQUE,
    contenido           text NOT NULL,
    instante            timestamptz NOT NULL,
    publicado_en        timestamptz
);
CREATE INDEX ix_bandeja_pendientes ON bandeja_salida (secuencia) WHERE publicado_en IS NULL;

-- Permisos -----------------------------------------------------------------------------------
GRANT SELECT ON desastres_locales, evaluaciones_validadas TO reconstruccion_app;
GRANT SELECT, INSERT, UPDATE ON proyectos, evaluaciones_origen, bandeja_salida TO reconstruccion_app;
GRANT SELECT, INSERT ON lotes_creacion TO reconstruccion_app;
-- Historial, avances, costos y documentos: solo leer e insertar. Nadie los modifica ni los borra.
GRANT SELECT, INSERT ON historial, registros_avance, registros_costo, documentos TO reconstruccion_app;
