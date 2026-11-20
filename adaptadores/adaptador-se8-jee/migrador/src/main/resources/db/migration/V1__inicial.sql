-- Esquema se8 del adaptador de SE-8 (AD-66). Flyway lo aplica como dueño; el rol ${rolApp} solo lee, inserta y actualiza.

-- Estado de entrega de cada alerta enviada a SE-8 (QA-08, RF-GD-15).
-- Máquina de estados: PENDIENTE -> ENVIADA -> ENTREGADA (solo con acuse) | FALLIDA; ENVIADA sin acuse -> PENDIENTE (reenvío).
CREATE TABLE entregas (
    id                 uuid        PRIMARY KEY,
    clave_envio        text        NOT NULL UNIQUE,      -- clave de idempotencia hacia SE-8
    clave_idempotencia text        NOT NULL,             -- clave del evento de origen
    tipo_origen        text        NOT NULL CHECK (tipo_origen IN ('EV-03', 'EV-04', 'EV-28')),
    id_evento_origen   uuid        NOT NULL,
    id_notificacion    uuid,                             -- solo EV-28
    intento            integer,                          -- solo EV-28
    id_desastre        uuid        NOT NULL,
    canal              text        NOT NULL CHECK (canal IN ('CORREO', 'SMS')),
    destino            text        NOT NULL,
    asunto             text        NOT NULL,
    mensaje            text        NOT NULL,
    severidad          text        NOT NULL,
    estado             text        NOT NULL CHECK (estado IN ('PENDIENTE', 'ENVIADA', 'ENTREGADA', 'FALLIDA')),
    envios             integer     NOT NULL DEFAULT 0,
    creada_en          timestamptz NOT NULL,
    ultimo_envio_en    timestamptz,
    acusada_en         timestamptz,
    secuencia_acuse    bigint,
    motivo             text,
    version            integer     NOT NULL DEFAULT 0,
    -- QA-08: nunca ENTREGADA sin acuse de SE-8. La base lo impide aunque el código fallara.
    CONSTRAINT ck_entregada_con_acuse CHECK (estado <> 'ENTREGADA' OR acusada_en IS NOT NULL)
);
CREATE INDEX ix_entregas_por_reenviar ON entregas (ultimo_envio_en) WHERE estado IN ('PENDIENTE', 'ENVIADA');
CREATE INDEX ix_entregas_desastre ON entregas (id_desastre);

-- Consumo idempotente (AD-02): una fila por evento aplicado, en la misma transacción que su efecto.
CREATE TABLE mensajes_procesados (
    clave_idempotencia text        PRIMARY KEY,
    id_evento          uuid        NOT NULL,
    tipo               text        NOT NULL,
    procesado_en       timestamptz NOT NULL
);

-- Fallos de aplicación por mensaje: a los 5 el consumidor lo aparta a dlq.salida.se8 (igual que ConsumidorIdempotente).
CREATE TABLE intentos_consumo (
    clave_idempotencia text        PRIMARY KEY,
    fallos             integer     NOT NULL,
    ultimo_error       text,
    actualizado_en     timestamptz NOT NULL
);

-- Bandeja de salida (AD-45): mismas columnas que Plataforma.Comun.Mensajeria (FilaBandeja).
CREATE TABLE bandeja_salida (
    id                  uuid        PRIMARY KEY,
    secuencia           bigint      GENERATED ALWAYS AS IDENTITY,
    secuencia_productor bigint      UNIQUE,
    tipo                text        NOT NULL,
    clave_ruteo         text        NOT NULL,
    clave_idempotencia  text        NOT NULL UNIQUE,
    id_correlacion      text,
    sobre               jsonb       NOT NULL,
    instante            timestamptz NOT NULL,
    orden               integer     NOT NULL,
    publicado_en        timestamptz
);
CREATE INDEX ix_bandeja_por_numerar ON bandeja_salida (instante, orden) WHERE secuencia_productor IS NULL;
CREATE INDEX ix_bandeja_por_publicar ON bandeja_salida (secuencia_productor)
    WHERE secuencia_productor IS NOT NULL AND publicado_en IS NULL;

CREATE TABLE contador_productor (
    productor text   PRIMARY KEY,
    ultimo    bigint NOT NULL
);

-- Marca de avance de la entrada (AD-14): último acuse de SE-8 conciliado.
CREATE TABLE marcas_avance (
    flujo          text        PRIMARY KEY,
    marca          text,
    actualizada_en timestamptz NOT NULL
);

-- Estado observable de la integración (E-15). Cambia en la misma transacción que el EV-23 que lo anuncia.
CREATE TABLE estado_integracion (
    sistema        text        PRIMARY KEY,
    estado         text        NOT NULL CHECK (estado IN ('CERRADO', 'ABIERTO')),
    abierto_desde  timestamptz,
    actualizado_en timestamptz NOT NULL
);
