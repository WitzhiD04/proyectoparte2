# Servicio de Evaluación de Daños

Recibe por lotes las evaluaciones que los evaluadores registraron sin conexión, detecta conflictos y duplicados sin
resolverlos (FP-06, QA-14) y atiende la revisión humana: validación, resolución de conflictos y catálogo.

**Origen.** El código viene del prototipo `danos/api`, commit `e1f33c0` ("Deja DISENO.md igual de corto que el documento
LaTeX"), que verificó QA-14 (SAD, Anexo A). La regla de conflicto, el detector de duplicados, el validador y la
sincronización conservan su comportamiento: `ReglaConflicto.cs`, `DetectorDuplicados.cs`, `ValidadorEvaluacion.cs`,
`Haversine.cs` y `Limites.cs` son los archivos del prototipo con otro espacio de nombres; `SincronizarLote.cs` es
`ServicioSincronizacion.cs` con el almacén detrás de un puerto. La app móvil (`danos/app`) no cambia.

Para ejecutar la plataforma, vea el [README de la raíz](../../README.md). La imagen corre como servicio (sin argumentos)
o como migrador (`migrar [--sembrar]`, DT-19).

## Estructura (arquitectura limpia y hexagonal, AD-08, DA-05)

| Carpeta | Anillo | Contenido |
|---|---|---|
| `src/EvaluacionDanos.Dominio` | Entidades y reglas | Entidades, operaciones de la app, `ReglaConflicto`, `DetectorDuplicados`, `ValidadorEvaluacion`, `ReglasRevision`, `CatalogoTipos`, `Desastre` (copia local). Sin paquetes. |
| `src/EvaluacionDanos.Aplicacion` | Casos de uso y puertos | Un caso de uso por operación, puertos de salida (`IAlmacenSincronizacion`, `IPublicadorHechos`, `IAlmacenEvidencias`...), hechos y DTO. Sin paquetes y sin conocer el bus. |
| `src/EvaluacionDanos.Infraestructura` | Adaptadores | EF Core con PostGIS y su migración, MinIO, `PublicadorHechosBandeja` (hechos → eventos canónicos), `ManejadorEventosEntrantes` (eventos → copias locales). Usa `comun/`. |
| `src/EvaluacionDanos.Api` | Adaptadores de entrada | Controllers, autorización de nivel 1 (`RequiereRol`) y `Program.cs`, donde se conecta cada puerto con su adaptador. |
| `tests/EvaluacionDanos.Pruebas` | | Las pruebas unitarias del prototipo, las reglas nuevas del dominio y la prueba de arquitectura. |
| `tests/EvaluacionDanos.Integracion` | | Las pruebas de integración del prototipo y las de la plataforma, con PostGIS y MinIO reales y dos réplicas (Testcontainers). |

## Dónde está cada parte del diseño

| Diseño | Código |
|---|---|
| Sincronización por lotes, una transacción por operación, reintento ante carrera (FP-06, QA-14) | `Aplicacion/CasosDeUso/SincronizarLote.cs` |
| Regla determinista de conflicto (AD-43, RF-ED-13) | `Dominio/ReglaConflicto.cs` |
| Reenvío idempotente (AD-44) | `SincronizarLote.IntentarAsync` (paso 1) y tabla `operacion_procesada` |
| Duplicados por distancia y ventana (AD-33, RF-ED-09) | `Dominio/DetectorDuplicados.cs`; candado por tipo en `AlmacenEvaluacionesEf.BloquearTipoAsync` |
| Validación de la operación (RF-ED-02) | `Dominio/ValidadorEvaluacion.cs` |
| Historial versionado de solo inserción (AD-30, DT-02) | `GRANT SELECT, INSERT` de la migración `Inicial` sobre `evaluacion_version`, `activo_version`, `operacion_procesada` y `evidencia_vinculada`; además `EvaluacionDanosDbContext.RechazarCambiosAlHistorial` |
| Cambio y evento en una transacción (AD-45) | `IPublicadorHechos` + `PublicadorHechosBandeja` (misma unidad de trabajo que el cambio) |
| Eventos EV-08, EV-09, EV-10, EV-22, EV-25 y EV-26 con el sobre canónico (E-02, E-03, E-05, E-06, E-13, E-16) | `Infraestructura/Mensajeria/PublicadorHechosBandeja.cs`; hechos en `Aplicacion/Hechos.cs` |
| Enumeraciones canónicas (E-08) | `MapeoCanonico` (el tipo de activo viaja en mayúsculas: `vivienda` → `VIVIENDA`) |
| Validación con regla de nivel 2 (RF-ED-08, DA-09, AD-22) | `Dominio/ReglasRevision.Validacion` y `CasosDeUso/Revision.cs` (`ValidarEvaluacion`) |
| Resolución y fusión sin eliminar versiones (AD-32, AD-33, E-14) | `Dominio/ReglasRevision.Resolucion` y `CasosDeUso/Revision.cs` (`ResolverConflicto`) |
| Registro con conexión, actualización y asociación (RF-ED-01, RF-ED-07, AD-29) | `CasosDeUso/Actualizaciones.cs` |
| Catálogo en ejecución (AD-28, DA-18, QA-10) | `Dominio/CatalogoTipos.cs`, `CasosDeUso/Catalogo.cs`; catálogo inicial en `SiembraCatalogo` (`Persistencia/Migracion.cs`) |
| Municipio del activo en el servidor (DA-07, E-12) | `AlmacenEvaluacionesEf.MunicipioDeAsync` (`ST_Contains`; si cae fuera, el más cercano); columna `activo.ubicacion` (`geometry(Point,4326)`) |
| Capa de municipios por EV-21 (E-11) | `ManejadorEventosEntrantes`, `CopiasLocalesEf.GuardarParteDeCapaAsync`, tablas `division_administrativa` y `capa_parte` |
| Copia local de desastres por EV-03, EV-05, EV-06 y EV-27 (E-29, E-25) | `Dominio/Desastre.cs`, `CasosDeUso/CopiasLocales.cs`, `ManejadorEventosEntrantes` |
| Descarga filtrada e incremental (DA-19, E-26) | `CasosDeUso/Consultas.cs` (`ConsultarDescarga`) |
| Evidencias en el almacén de objetos (DA-06, RF-ED-03) | `CasosDeUso/SubirEvidencia.cs`, `Infraestructura/Archivos/AlmacenEvidenciasMinio.cs`; clave `evaluacion/{id}/{idEvidencia}` |
| Rol de mínimo privilegio y migrador (DT-02, DT-19) | `Persistencia/Migracion.cs` y la migración `Inicial` |
| Relevo con secuencia sin huecos y consumidor idempotente | `comun/Plataforma.Comun.Mensajeria` |
| Dos réplicas sin sesión (AD-24) | Estado solo en la base y en MinIO; candado de duplicados en PostgreSQL; `despliegue/` |

## Rutas

| Método | Ruta | Rol | Notas |
|---|---|---|---|
| GET | `/v1/descarga?desastreId=&zona=&desde=` | evaluador | App. `zona`: DIVIPOLA de municipio o departamento. `desde`: el `instanteServidor` de la descarga anterior |
| POST | `/v1/sincronizacion/evaluaciones` | evaluador | App. 503 si el lote se corta: la app lo reenvía completo |
| PUT | `/v1/evidencias/{id}` | evaluador | App. Multipart, parte `archivo` |
| GET | `/v1/evaluaciones?idDispositivo=` | evaluador, operador, autoridad, organismo | También acepta `dispositivo=` (E-09) |
| POST | `/v1/evaluaciones` | evaluador | Registro con conexión: una operación por el mismo camino. 201, o 422 si es inválida |
| PATCH | `/v1/evaluaciones/{id}` | evaluador (las de su dispositivo), operador | Versión nueva; EV-26 |
| POST | `/v1/evaluaciones/{id}/validacion` | autoridad, organismo | `{estado, motivo}`; la organización debe participar en el desastre; EV-08 |
| POST | `/v1/evaluaciones/{id}/asociacion` | operador, evaluador | `{desastreId}`; EV-26 |
| GET | `/v1/conflictos?estado=` | operador | Ambas versiones completas |
| POST | `/v1/conflictos/{id}/resolucion` | operador | `{decision, justificacion[, vigente]}`; EV-22 |
| GET | `/v1/catalogos/tipos-activo` | cualquier identidad | Catálogo con criterios por nivel |
| POST, PATCH | `/v1/catalogos/tipos-activo[/{codigo}]` | administrador | EV-25 en cada cambio |
| GET | `/salud`, `/salud/vivo`, `/salud/listo` | — | `/salud` es el que prueba la app |

Errores: `{ "error": "..." }`. 400 dato inválido, 401 sin identidad, 403 sin el rol o sin autorización de nivel 2,
404 no existe, 409 otro cambio llegó primero, 422 la regla no lo permite, 503 reintente.

## Decisiones que conviene conocer

- **Sin la capa de municipios no se sincroniza.** EV-09 exige `codigoMunicipio`. Mientras no llegue completa la capa
  (las 33 partes de EV-21), `/salud/listo` falla y la réplica no recibe tráfico. Si aun así llega un lote, responde 503
  sin procesar nada: la app lo conserva en su cola y lo reenvía. Nunca se marca una operación como inválida por esto.
- **Un cambio de contenido hecho en el servidor es una versión del activo.** La actualización con conexión, la
  asociación y la resolución de un conflicto suben la versión del activo (validar y rechazar no la suben). La regla de
  conflicto no cambió: una edición hecha sin conexión sobre una base anterior queda en conflicto y va a revisión, en vez
  de sobrescribir en silencio lo que cambió el servidor (AD-32).
- **Si cambia el contenido de una evaluación validada, u otra la reemplaza al resolver un conflicto, la validación se retira** y se publica EV-08 con `PENDIENTE`.
- **Identidad.** En compose el servicio corre con `Identidad__Modo=Jwt`: la app (v2.1.0) entra por el gateway con el
  token OIDC de Keycloak y `X-Dispositivo`. El modo por encabezados (`RolesEfectivos` en `Api/Seguridad/Autorizacion.cs`
  trata como `evaluador` una petición sin roles que trae `X-Dispositivo`) queda solo para las pruebas de integración.
- **Descarga.** La app envía `zona` (municipio DIVIPOLA) y `desde` (descarga incremental). Sin filtros entrega todos los
  activos. El catálogo y los desastres van siempre completos; `desde` solo reduce los activos, con un traslape de 60 s.

## Probar

```bash
bash herramientas/dotnet.sh test servicios/evaluacion-danos/tests/EvaluacionDanos.Pruebas
bash herramientas/dotnet.sh test servicios/evaluacion-danos/tests/EvaluacionDanos.Integracion
```

La prueba `QA14_con_dos_replicas...` ejecuta el simulador (`simuladores/simulador-qa14`) repartiendo las peticiones
entre dos instancias del servicio. El simulador también corre solo contra un servicio desplegado:
`bash herramientas/dotnet.sh run --project simuladores/simulador-qa14 -- --url http://<host>:8080`.
