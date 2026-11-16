# Servicio de Auditoría

Implementa el microservicio de Auditoría centralizado del SAD (AD-21) según la decisión DA-17 de la revisión de
arquitectura: consume todos los eventos de cambio del bus, los guarda en un registro inmutable e idempotente, detecta
los eventos que no llegaron (mitigación de ATAM R-02) y ofrece una consulta de solo lectura del rastro: quién, qué hizo,
cuándo, sobre qué información y con qué resultado (QA-13, QA-15).

| Aspecto | Valor |
|---|---|
| Base | `auditoria` (rol `auditoria_app`) |
| Bus | Consume `q.auditoria` (enlazada a `#`) con el usuario `auditoria`. No publica. |
| Réplicas | 2 o más (compose y Kubernetes) |
| Migrador | La misma imagen con el argumento `migrar` (DT-19) |

Para ejecutar la plataforma, vea el [README de la raíz](../../README.md).

## Estructura (arquitectura limpia y hexagonal, AD-08)

| Carpeta | Anillo | Contenido |
|---|---|---|
| `src/Auditoria.Dominio` | Entidades | `EntradaAuditoria` (inmutable), `SeguimientoProductor` y `HuecoSecuencia` (R-02), `PoliticaConsulta`. Sin paquetes. |
| `src/Auditoria.Aplicacion` | Casos de uso y puertos | `RegistrarEvento`, `ConsultarRegistro`, `ConsultarHuecos`, `RevisarSecuencias`; puertos de solo inserción y lectura. Sin paquetes y sin conocer el bus. |
| `src/Auditoria.Infraestructura` | Adaptadores | EF Core y su migración (tabla particionada), `ManejadorEventosAuditoria` e `InterpretacionEventos` (sobre canónico → caso de uso), procesos periódicos. Usa `comun/`. |
| `src/Auditoria.Api` | Adaptadores de entrada | Controller de solo lectura, presenter y `Program.cs`, donde se conecta cada puerto con su adaptador. |
| `tests/Auditoria.Pruebas` | | Dominio, casos de uso con puertos en memoria y la prueba de arquitectura. |
| `tests/Auditoria.Integracion` | | PostgreSQL y RabbitMQ reales (Testcontainers, topología de `despliegue/bus`): migración, particiones, permisos, cada tipo del catálogo, atraso de la cola, QA-15 y R-02. |

## Dónde está cada parte del diseño

| Diseño | Código |
|---|---|
| Registro inmutable (AD-21, AD-41, QA-13 b) | `EntradaAuditoria` (sin métodos que la cambien), `IRegistroAuditoria` (solo `Agregar` y consultar) y los `GRANT SELECT, INSERT` de la migración inicial: el rol del servicio no puede modificar, borrar, truncar ni separar particiones |
| Particiones mensuales y retención de 5 años o más (QA-13 e) | Migración inicial (`registro ... PARTITION BY RANGE (instante_emision)`, particiones del año en curso y del siguiente, partición por omisión `registro_fuera_de_rango`) y `ProcesoParticiones`, que cada día llama a `asegurar_particiones_registro` para tener 12 meses creados por adelantado |
| Alimentado solo por el bus, sin ruta de escritura (AD-21) | `AuditoriaController` solo tiene `GET`; una prueba recorre todas las rutas y verifica que no hay otro método |
| Consumo idempotente (DA-03, AD-21) | `ConsumidorIdempotente` de `comun/` por `claveIdempotencia`; `RegistrarEvento` además por `id_evento`; la llave `(id_evento, instante_emision)` es la tercera barrera |
| Quién, qué, cuándo, sobre qué, resultado, valor anterior y nuevo (QA-13, QA-15 a) | `InterpretacionEventos`: un caso por tipo del catálogo. Un tipo o una versión que no conoce se registra igual, con actor y entidad nulos (QA-09, R-03) |
| Rechazos de autorización (SAD §7.1, DA-15) | `ResultadoOperacion.Rechazado` y el `CHECK` de `resultado`. Falta el evento en el catálogo; cuando exista, solo se agrega su caso en `InterpretacionEventos` |
| Detección de huecos (ATAM R-02, E-18, E-31) | `SeguimientoProductor.Revisar` (dominio), `RevisarSecuencias` (caso de uso), `ProcesoRevisionHuecos` (cada 10 s, una réplica a la vez con `pg_try_advisory_xact_lock`), tablas `seguimiento_productor` y `huecos_secuencia`, alerta `HUECO_DETECTADO` en `AvisosHuecosPorLog` |
| Plazo de un hueco | `Auditoria:PlazoHuecoSegundos` (60 por omisión): un hueco se reporta solo si persiste más que eso, porque dos réplicas del relevo pueden publicar fuera de orden |
| Consulta del rastro (DA-17, QA-13 d) | `ConsultarRegistro` y `RegistroAuditoriaEf.ConsultarAsync`, con índices por entidad, actor, tipo, instante y correlación (DA-20) |
| Solo auditor y autoridad (DA-17, AD-22) | `PoliticaConsulta`; la identidad llega por `IIdentidadActual` de `comun/Plataforma.Comun.Seguridad` |
| QA-15 (b y c) | Prueba `QA15_un_proyecto_con_N_cambios_...`: N cambios dan N entradas y cada una se puede consultar en 5 s o menos desde su emisión |

## Rutas

| Método | Ruta | Qué devuelve |
|---|---|---|
| GET | `/v1/auditoria?entidad=&idEntidad=&actor=&tipo=&idCorrelacion=&desde=&hasta=&pagina=&tamano=` | El rastro en orden cronológico de emisión, paginado (`tamano` de 1 a 1000, 100 por omisión). `desde` y `hasta` son instantes ISO-8601 con zona e incluyen los extremos |
| GET | `/v1/auditoria/huecos` | Por productor: mayor número contiguo, mayor recibido, números faltantes ya reportados y los que todavía están en plazo; y la lista de huecos reportados (`ABIERTO` o `CUBIERTO` si el evento llegó tarde) |
| GET | `/salud/vivo` y `/salud/listo` | Sondeos del orquestador (AD-24). Listo: base y cola `q.auditoria` disponibles |

Ambas rutas exigen identidad (401) y el rol `auditor` o `autoridad` (403). Ejemplo, mientras la identidad viaja en
encabezados:

```bash
curl -H "X-Usuario: contraloria" -H "X-Roles: auditor" \
  "http://auditoria:8080/v1/auditoria?entidad=proyecto&idEntidad=<id>"
```

Valores de `entidad`: `desastre`, `evaluacion`, `conflicto`, `proyecto`, `tipo-activo`, `capa-geografica` e
`integracion`. Los actores que no son personas se escriben `sistema:<productor>` o `dispositivo:<id>`.

## Esquema de la base (migraciones)

La migración está en `src/Auditoria.Infraestructura/Persistencia/Migraciones`. La tabla `registro` está escrita a mano
dentro de la migración, porque EF Core no crea tablas particionadas; tiene las mismas columnas que el modelo y una
prueba verifica que el modelo no tenga cambios pendientes. Para agregar una migración, desde la raíz del repositorio:

```bash
P=servicios/auditoria/src/Auditoria.Infraestructura
bash herramientas/dotnet.sh ef migrations add <Nombre> --project $P --startup-project $P --context AuditoriaDbContext --output-dir Persistencia/Migraciones
```

Si la migración crea tablas, agregue en ella los `GRANT` del rol del servicio (mínimo privilegio, DT-02). Para archivar
un mes ya vencido, el dueño de la base ejecuta `ALTER TABLE registro DETACH PARTITION registro_AAAA_MM` y lo respalda;
el servicio no puede hacerlo.

## Lo que todavía no está

- **El evento de rechazo de autorización** del gateway y de los servicios (SAD §7.1, DA-15): no está en el catálogo.
- **El autor de EV-18:** el evento no trae quién creó el proyecto, así que esa entrada queda con actor nulo.
- **La jurisdicción de la autoridad** (DA-14): hoy toda autoridad ve todo el rastro.
- **El gateway con identidad real** (YARP + Keycloak): hasta entonces la identidad viaja en encabezados.
