-- Base de datos de la Consulta del Programa (modelo de lectura). Es otra base: cada servicio es dueño de sus datos.
CREATE ROLE programa_app LOGIN PASSWORD 'programa_local';
CREATE DATABASE programa;
\connect programa

CREATE TABLE proyeccion_proyectos (
    id_proyecto      uuid PRIMARY KEY,
    id_desastre      uuid NOT NULL,
    nombre           text NOT NULL,
    estado           text NOT NULL,
    presupuesto      numeric(18, 2) NOT NULL,
    costo_ejecutado  numeric(18, 2) NOT NULL,
    avance           numeric(5, 2) NOT NULL,
    version          integer NOT NULL
);
CREATE INDEX ix_proyeccion_desastre ON proyeccion_proyectos (id_desastre);

GRANT SELECT, INSERT, UPDATE ON proyeccion_proyectos TO programa_app;
