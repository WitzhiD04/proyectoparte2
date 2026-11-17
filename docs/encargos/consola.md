# Encargo · Consola de coordinación unificada en Angular (DA-13)

> **Alcance de esta entrega (fijado el 2026-11-17).** Se implementan Móvil, Evaluación de Daños, Mapa y visualización
> de desastres, y Reconstrucción. Esta nota recorta el encargo y manda sobre el resto de su texto.
>
> - **Solo** el inicio de sesión, el interceptor, el menú por roles y los módulos `mapa`, `evaluaciones` y
>   `reconstruccion`. **Fuera:** los módulos `desastres`, `auditoria` e `integraciones`.
> - El módulo `mapa` va completo, con sus tres capas (la tercera es la zona del desastre con su barra de tiempo).
> - Recorrido final: el desastre y su zona se registran por la API (`pruebas/humo/prueba-desastres.sh`), no desde la
>   consola. La validación y el proyecto se hacen en la consola. El paso del auditor queda fuera.

Lea primero `docs/encargos/convenciones.md`. Alcance: `consola/` (Angular 22) y, si lo necesita, la entrada `web` de
compose y k8s.

**Prerrequisitos:**
- **Keycloak** con el cliente `consola`.
- **El gateway** (`docs/encargos/gateway.md`). Sin él, durante el desarrollo puede apuntar a nginx con servicios en modo
  `Jwt`.

## Qué es

Hoy `consola/` solo cubre Reconstrucción y usa un selector de perfil que manda encabezados `X-Rol`. DA-13
(`docs/decisiones/REVISION_ARQUITECTURA.md`) pide **una sola consola** con:
- **Inicio de sesión OIDC con PKCE contra Keycloak**, cliente `consola`. Los menús se arman con los roles del token y se
  **elimina el selector de perfil**.
- **Módulos de carga diferida por área:**

| Módulo | Qué muestra | Rutas |
|---|---|---|
| `desastres` | Registro con los 12 atributos del Cuadro 7, reclasificación de severidad con justificación, zona por municipios o **por polígono dibujado en el mapa**, transiciones posibles según el perfil, organismos e historial | Gestión de Desastres: `/v1/desastres*`, `/v1/catalogos/*` |
| `mapa` | **Leaflet** con tres capas superpuestas: (1) coropleta por municipio del índice de daño, de `/v1/consulta-danos/mapa?desastre=`; (2) puntos de los activos por nivel, con marca de validada y de conflicto pendiente (`puntos=true`); (3) la zona del desastre con sus versiones y **una barra de tiempo** para ver su evolución (`/v1/consulta/mapa`, `/v1/consulta/desastres/{id}/evolucion?en=`). Cada capa muestra su `datoAl` y un aviso si viene `degradado` | Consulta y Consulta de Daños |
| `evaluaciones` | Validación (rechazo con motivo), bandeja de conflictos con las cuatro decisiones (FUSIONAR con la elección de la vigente), catálogo de tipos de activo para el administrador, consolidado con priorización e indicador de R-08 | Evaluación de Daños: `/v1/evaluaciones*`, `/v1/conflictos*`, `/v1/catalogos/tipos-activo`; Consulta de Daños |
| `reconstruccion` | Lo que ya existe, migrado al login | `/v1/reconstruccion/*`, `/v1/proyectos*`, `/v1/programa/*` |
| `auditoria` | El rastro con filtros y los huecos de secuencia | `/v1/auditoria`, `/v1/auditoria/huecos` |
| `integraciones` | El estado de cada sistema externo: "SE-7 no responde desde hh:mm" (QA-01 b) | `/v1/integraciones` |

Los cuerpos exactos de las peticiones están en `pruebas/humo/*.sh` y en el README de cada servicio.

## Reglas

- Las tildes y los textos van en español.
- Un **interceptor** agrega `Authorization: Bearer` y renueva el token.
- Ante un 403, la consola muestra el mensaje del servicio; no esconde la regla.
- Las licencias de las capas de mapa base (OpenStreetMap) llevan su atribución.

## Criterio de salida

- `ng build` sin errores y las pruebas de componentes que agregue.
- El recorrido completo con 4 usuarios de Keycloak, **sin selector de perfil**:
  1. `ungrd.operador`: registra el desastre y su zona.
  2. `bomberos.operador`: valida las evaluaciones.
  3. Una autoridad LOCAL con `territorio` 63001 (agréguela al realm si no existe): crea el proyecto y lo aprueba.
  4. `auditor.1`: ve el rastro.
- Capturas del mapa con las tres capas en el README de la consola.
- Reporte final con el formato de `convenciones.md`.
