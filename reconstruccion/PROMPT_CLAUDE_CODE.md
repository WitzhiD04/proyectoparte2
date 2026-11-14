# Verificación final del módulo de Reconstrucción (para Claude Code, en el PC con .NET 10 y Docker)

## Contexto

Este repositorio implementa el módulo de Reconstrucción con arquitectura limpia, siguiendo el documento de
diseño. El alcance es solo Reconstrucción: los otros módulos, el API Gateway y los sistemas externos no
existen todavía.

| Parte | Estado |
|---|---|
| `src/Reconstruccion.Dominio`, `src/Reconstruccion.Aplicacion` | Compilados. Las 43 pruebas pasan. |
| `src/Reconstruccion.Api` | Compilado y probado por HTTP contra una infraestructura en memoria. |
| `src/Reconstruccion.Infraestructura` | Compila en el PC. Falta probarlo contra PostgreSQL, RabbitMQ y MinIO reales. |
| `consola-web` | Compilada con `ng build` y probada en navegador contra la API. |
| `db/*.sql` y `db/04-replicacion.sh` | Probados en PostgreSQL: tablas, permisos del historial y réplica con `pg_basebackup`. |
| `Dockerfile`, `docker-compose.yml`, `k8s/` | **Nunca ejecutados.** Solo se validó la sintaxis. |

## Reglas

- No cambies el diseño: ni las reglas de `Dominio`, ni los casos de uso, ni los puertos de `Aplicacion`.
  Si un error parece exigir cambiar un puerto, detente y pregunta.
- `Dominio` y `Aplicacion` no pueden recibir referencias a paquetes NuGet.
- El historial es de solo inserción: no agregues `UPDATE` ni `DELETE` sobre `historial`, ni más permisos al
  usuario `reconstruccion_app`. Las copias locales (`desastres_locales`, `evaluaciones_validadas`) son de
  solo lectura para el servicio: se llenan con `db/03-datos-prueba.sql`.
- **No implementes** consumidores de eventos de otros módulos ni del sistema de proyectos externo, ni los
  otros módulos, el API Gateway, inicio de sesión o los sistemas externos. Están fuera del alcance.
- Si cambias algo en `db/`, regenera `k8s/05-esquema.yaml` con `bash scripts/generar-esquema-k8s.sh`.
- Código y comentarios en español, como el resto.

## Pasos

1. `dotnet build` y `dotnet test`. Deben pasar las 43 pruebas.
2. `docker compose down -v` (para que los scripts de `db/` corran desde cero) y `docker compose up -d --build`.
   Revisa con `docker compose logs` que arranquen sin excepciones:
   - `reconstruccion` (2 copias): el relevo de eventos debe conectarse a RabbitMQ.
   - `consulta`: el consumidor de la proyección debe conectarse a la cola `programa.proyeccion`.
   - `db-replica`: debe terminar la copia inicial y quedar en modo réplica.
3. `bash scripts/prueba-humo.sh` (en Windows, con Git Bash o WSL). Deben pasar todas las verificaciones.
4. Verifica a mano y repórtame el resultado de cada punto:
   - Como `reconstruccion_app`, `UPDATE historial SET autor = 'x'` y `DELETE FROM historial` deben fallar
     por permisos, igual que `INSERT INTO desastres_locales ...`.
   - `SELECT count(*) FROM bandeja_salida WHERE publicado_en IS NULL` debe quedar en 0 unos segundos
     después de la prueba.
   - En el panel de RabbitMQ (http://localhost:15672) debe existir el intercambio `reconstruccion.eventos`
     y la cola `programa.proyeccion` de tipo quorum.
   - En el panel de MinIO (http://localhost:9001) debe estar el documento en `reconstruccion-documentos`.
   - En `db-replica`, `SELECT pg_is_in_recovery()` debe dar `t` y los proyectos creados en la prueba deben
     aparecer también ahí.
   - Detén una de las dos copias de `reconstruccion` y confirma que la consola sigue funcionando.
   - Detén `bus`, registra un cambio en un proyecto (debe guardarse), vuelve a levantar `bus` y confirma
     que el evento se publica y el consolidado se actualiza.
5. Abre http://localhost:8080 y recorre el flujo con los tres perfiles (ver README).
6. Opcional: construye las imágenes y aplica `kubectl apply -k k8s/` en el Kubernetes de Docker Desktop.

## Puntos donde es más probable un error

- `RelevoEventos`: usa `FromSqlInterpolated` con `FOR UPDATE SKIP LOCKED` dentro de una transacción y
  `CreateChannelOptions(true, true)` para las confirmaciones del bus.
- `RepositorioProyectosEf.GuardarAsync`: decide si el proyecto es nuevo buscando la fila en
  `_db.Proyectos.Local`. La columna `version` es el control de concurrencia.
- `LoteCreacionFila.IdsProyectos` es `Guid[]` sobre una columna `uuid[]`.
- `bandeja_salida.secuencia` es una columna de identidad: EF no debe enviarla al insertar.
- En PostgreSQL 18 el volumen se monta en `/var/lib/postgresql`.
- `db-replica` corre como root para `pg_basebackup` y luego arranca con el `docker-entrypoint.sh` de la
  imagen, que corrige los permisos de la carpeta de datos.
- Los scripts de `db/` solo corren la primera vez que se crea el volumen. Si cambias uno, usa
  `docker compose down -v`.

Al terminar, dame la lista de archivos que cambiaste y por qué.
