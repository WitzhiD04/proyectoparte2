# Revisión de arquitectura y plan de integración

Plataforma para la Gestión de Desastres y Reconstrucción. Revisión de los repos `proyectoparte2` (Reconstrucción) y `danos` (Evaluación de Daños y app móvil) contra el enunciado, `A_IMPLEMENTAR.md`, el SRS 1.1, el SAD 1.1, el ATAM 1.1 y el Documento de diseño del módulo de Reconstrucción.

Fecha: 16 de noviembre de 2026.

Documento complementario: [ENMIENDAS_DOCUMENTOS.md](ENMIENDAS_DOCUMENTOS.md), con las correcciones que habría que hacer a los documentos. Los documentos no se modificaron.

---

## Estado de la implementación

Actualizado el 18 de noviembre de 2026 (rama `cierre-implementacion` de `proyectoparte2` y de `danos`). Los pasos 1 a 5
del plan y los encargos de `docs/encargos/LEEME.md` están hechos dentro del alcance de la entrega: a) móvil, registro y
caracterización de un desastre; b) reconstrucción física; c) mapa y visualización; d) evaluación de daños. El diseño
completo (12 microservicios, 8 adaptadores, dos regiones) sigue en el SAD; lo que no se implementó se declara abajo.

**Verificación.** Suite de .NET: 1 008 pruebas en 19 proyectos, todas en verde (Auditoría y Consulta de integración y
`Plataforma.Comun.Pruebas` se repitieron por separado porque en la corrida conjunta la API de Docker respondió 500 al
crear contenedores de prueba). App: 81 pruebas unitarias y 16 instrumentadas en verde, 7 de ellas contra el gateway y
Keycloak desde el emulador. En compose pasan las pruebas de humo `prueba-humo.sh`, `prueba-desastres.sh` y
`prueba-qa11.py`.

### Medidas

Corridas en compose en un solo computador (Windows 11, Docker Desktop, unos 30 contenedores y el emulador de Android
encendido), a través del gateway y con tokens reales de Keycloak. Los números de latencia son de un perfil local y
varían entre corridas; no reemplazan la prueba de carga con 10 000 usuarios.

| Escenario | Medida del SRS | Cómo se midió | Resultado | ¿Cumple? | Observaciones |
|---|---|---|---|---|---|
| QA-14 | 0 pérdidas, 0 sobrescrituras silenciosas, 100 % de conflictos detectados, reenvío sin duplicados, 120 evaluaciones en ≤ 10 min | `simuladores/simulador-qa14 --url <gateway> --keycloak http://localhost:8180` | 115 aplicadas y 5 en conflicto; 0 pérdidas, 0 sobrescrituras, 0 duplicados; resultados idénticos en el reenvío; 120 de 120 fotos; 27,8 s | Sí | Banco de 5 conflictos por corrida; la regla de conflicto tiene además 7 pruebas unitarias y 21 de sincronización |
| QA-14 (app) | Igual, desde el cliente real | `ContratoServidorTest` en el emulador contra el gateway y Keycloak (`adb reverse tcp:8180 tcp:8180`) | 7 de 7 pruebas en verde: activo nuevo con foto, edición sin red, conflicto entre dos dispositivos, inválida corregida, descarga con tildes, registro de desastre idempotente y rechazo por rol | Sí | Repositorio `danos` |
| QA-11 | 100 % de intentos de un organismo no responsable rechazados sobre ≥ 200 casos; 0 datos modificados | `pruebas/humo/prueba-qa11.py` | 198 de 198 rechazados (100 %), 0 falsos negativos, 0 datos modificados | Sí en (a) y (b) | El banco tiene 198 casos, 2 menos que los 200 del SRS. La medida (c), registro del intento en Auditoría, no se implementó (L-02) |
| QA-06 | p95 < 2 s y p99 < 4 s en el punto de entrada; antigüedad del dato ≤ 2 s en el p95; marca de tiempo en el 100 % | `pruebas/medidas/latencias.py` (20 clientes, 300 peticiones por ruta) | Consolidado del desastre: p95 34 ms, p99 47 ms. Mapa de desastres: p95 142 ms, p99 159 ms. Mapa de daños: p95 1 628 ms, p99 1 935 ms. Marca `datoAl` en el 100 %. Antigüedad del dato (30 muestras): p50 652 ms, p95 1 485 ms, máximo 11,8 s | Sí en el perfil local | No medido con 10 000 usuarios (L-04). En una corrida anterior la antigüedad dio p95 de 4,2 s con 10 muestras: depende de la carga de la máquina |
| QA-07 | p95 < 3 s y p99 < 6 s del registro con los atributos mínimos | `pruebas/medidas/latencias.py` (100 registros, 20 clientes) | p95 1 334 ms, p99 1 353 ms; 100 de 100 con 201 | Sí en el perfil local | La tasa del SRS es 50 registros/min; la prueba envía los 100 en ráfaga |
| QA-10 | 0 componentes modificados y 0 redespliegues para un tipo nuevo | `GestionDesastres.Integracion` (`Un_tipo_nuevo_se_usa_sin_redesplegar_QA10`) y `EvaluacionDanos.Integracion` (tipo de activo nuevo) | Pasan: el tipo se crea por la API y se usa en el registro siguiente | Sí | |
| QA-01 | Ninguna función ajena al sistema externo caído se degrada; la caída se refleja en ≤ 30 s; 100 % de lo encolado se procesa al volver | `AdaptadorSe6.Integracion` (12) y `AdaptadorSe7.Integracion` (14) con los simuladores | Pasan: cortacircuitos, EV-23 y reintento sin pérdida | Sí en pruebas de integración | El escalado de 0 a N con KEDA no se verificó en Kubernetes (L-05) |
| QA-04 | El fallo de un componente interno no se propaga; 0 eventos perdidos | `Plataforma.Comun.Pruebas` (71, con PostgreSQL y RabbitMQ reales) | Pasan: consumidor detenido sin pérdida, mensaje envenenado apartado, cola llena sin descarte | Sí en pruebas de integración | |
| QA-13 | 100 % de las operaciones producen entrada; 0 entradas modificables | `Auditoria.Integracion` (53) | Pasan | Sí en (a) y (b) | La medida (d), rastro de 10 000 entradas en < 10 s con k6, no se corrió (L-03) |
| QA-15 | 100 % de los cambios de presupuesto y estado con valor anterior y nuevo; consulta en ≤ 5 s | `Auditoria.Integracion` (`QA15_un_proyecto_con_N_cambios_...`) y `Reconstruccion.Integracion` | Pasan | Sí | |
| QA-02, QA-03, QA-05, QA-08, QA-09, QA-12 | — | — | No medidos | — | Disponibilidad mensual, dos regiones, 10 000 usuarios, alerta (SE-8), sistema externo nuevo y datos personales quedan fuera del alcance implementado (L-01, L-04, L-06) |

### Límites declarados

| ID | Límite | Motivo | Qué haría falta |
|---|---|---|---|
| L-01 | Una sola región: no hay enrutador global ni réplica en otra ubicación (AD-26, AD-27, QA-03) | Fuera del alcance de la entrega; el diseño está en el SAD | Overlays `region-a`, `region-b` y `global` en `despliegue/k8s` con CloudNativePG y réplica del bus |
| L-02 | Los rechazos del gateway y la alarma de la bandeja no se registran en Auditoría (QA-11 c, R-08) | Recorte del encargo del gateway | Evento aditivo de rechazo y su interpretación en Auditoría |
| L-03 | No hay pruebas de carga con k6 ni de caos (QA-05, QA-06 y QA-13 d) | No se ejecutó el encargo de carga y caos | `pruebas/carga-k6` y `pruebas/caos` según `pruebas/ENCARGO-carga-y-caos.md` |
| L-04 | Las latencias se midieron con 20 clientes en un computador, no con 10 000 usuarios | Una sola máquina | Carga con k6 en un clúster |
| L-05 | El despliegue en Kubernetes (Minikube) no se verificó de punta a punta; el inicio de sesión exige reenviar los puertos de la consola y de Keycloak a `localhost` | Tiempo de la entrega | `kubectl port-forward` o un Ingress con el emisor de Keycloak alineado |
| L-06 | Fuera de la implementación: Consolidación, Notificación (SE-8, QA-08), Gestión de Recursos, Personas Afectadas (QA-12), Coordinación de Organismos, Portal público y adaptadores SE-1, SE-3, SE-4, SE-5 y SE-8 | Alcance de la entrega: los cuatro mínimos | Quedan como diseño en el SAD |
| L-07 | La app de entrega es la variante `debug` (admite `http://`); la variante `release` no tiene una URL HTTPS configurada y Keycloak corre en `start-dev` sin TLS | Entorno de laboratorio | Un despliegue con TLS en el borde (mTLS entre servicios también está declarado como límite) |
| L-08 | El emisor de los tokens es `http://localhost:8180`: desde el emulador hace falta `adb reverse tcp:8180 tcp:8180`, y un celular físico exige cambiar `KC_HOSTNAME` e `Identidad__Emisor` a la vez | El `iss` del token debe coincidir con el que validan el gateway y los servicios | Un nombre DNS común para Keycloak |
| L-09 | Pendientes menores: filtro de Auditoría por jurisdicción, `ETag` en la descarga, aviso de activo fusionado al cliente incremental, base única de SE-2, índice de `p_historial_activo` por municipio, recarga de llaves JWT y evaluaciones rechazadas en la consola | Ver `docs/encargos/LEEME.md` | Cada uno está descrito allí con su corrección |
| L-10 | La app adjunta solo fotos (no documentos) como evidencia, y la evidencia no guarda un autor propio (RF-ED-03 parcial) | Alcance del prototipo | Tipo de evidencia "documento" y autor en la entidad |

El recorrido de uso de punta a punta, con la lista de capturas, está en [`docs/RECORRIDO.md`](../RECORRIDO.md).

---

## 0. Cómo leer este documento

### Convenciones

| Marca | Significado |
|---|---|
| `[SAD AD-xx]`, `[SRS RF-xx]`, `[ATAM R-xx]`, `[Diseño RC]`, `[Enunciado §n]` | Fuente que respalda la afirmación |
| **No está en los documentos** | Decisión o hecho que los documentos no cubren. Se justifica con atributos de calidad y se señala |
| **Supuesto** | Interpretación que tomé. Confírmela si no está de acuerdo |
| **Decidido por usted** | Elección que usted hizo en esta sesión |
| H-nn | Hallazgo (sección 4) |
| DA-nn | Decisión de arquitectura propuesta (sección 6) |
| DT-nn | Decisión de tecnología propuesta (sección 7) |
| E-nn | Enmienda a un documento (archivo complementario) |
| F-n | Fase del plan (sección 9) |
| P-nn | Pregunta abierta (sección 12) |

### Severidad de los hallazgos

| Severidad | Criterio |
|---|---|
| **Crítica** | Rompe un mínimo de `A_IMPLEMENTAR.md`, un flujo de punta a punta, o un escenario (A,A) del árbol de utilidad (QA-02, QA-03, QA-09, QA-14) |
| **Alta** | Incumple una decisión del SAD, una regla de negocio o un escenario de calidad de prioridad 6 |
| **Media** | Incumple una decisión de forma parcial, o es una mala práctica con impacto real |
| **Baja** | Higiene, nombres o deuda técnica sin impacto inmediato |

### Qué revisé y qué no

- **Revisado:** los cinco PDF (texto extraído), `A_IMPLEMENTAR.md`, todo el backend de Reconstrucción (dominio, casos de uso, infraestructura, API, SQL, compose y k8s), el backend de Danos (dominio, acceso e infraestructura), la estructura de la app Android y su contrato HTTP.
- **Ejecutado:** los dos sistemas levantan con `docker compose` y la app instala en el emulador.
- **No revisado línea por línea:** las pantallas de la app Android y de la consola Angular.
- **No ejecutado:** las pruebas automáticas (`dotnet test`, `gradlew test`).

### Corrección de algo que le dije antes

En mi informe anterior dije que la cadena entre módulos usaba "EV-09, evaluación validada". **Eso es incorrecto.** En el catálogo del SAD (Cuadro 16), **EV-08 es "Evaluación validada"** y **EV-09 es "Evaluación sincronizada"**. Este documento usa los identificadores correctos.

---

## 1. Resumen ejecutivo

**Estado actual.** Hay dos módulos bien construidos por dentro, pero **aislados entre sí**.

- **Reconstrucción** cubre sus 11 casos de uso: arquitectura limpia, outbox con relevo a RabbitMQ, historial de solo inserción garantizado por permisos de la base de datos, y CQRS con un servicio de consulta.
- **Danos** cubre FP-06 con calidad: réplica local, cola de operaciones inmutables, regla determinista de conflicto y reenvío idempotente.

Ninguno de los dos se comunica con el otro, ni con un gateway, ni con un sistema externo.

**Los 10 problemas más graves:**

1. **No hay integración entre módulos** (H-01). Danos guarda eventos que nadie publica. Reconstrucción lee "evaluaciones validadas" sembradas a mano por SQL. Los identificadores de desastre no coinciden.
2. **El flujo de validación de evaluaciones no existe** (H-10). Sin validación no hay EV-08, y sin EV-08 Reconstrucción no tiene candidatas. Así se rompe el escenario E del enunciado.
3. **No hay fuente de desastres** (H-02). Reconstrucción solo crea proyectos si el desastre está "En reconstrucción" (AD-40), pero nadie publica ese estado (EV-06).
4. **Los propios documentos tienen flujos incompletos** (H-03, H-04, H-05):
   - EV-09 no trae los datos que necesita el mapa.
   - EV-08 no trae el activo ni el municipio que Reconstrucción necesita.
   - Falta el evento de entrada de SE-7 (avance y contratista).
   - Falta un evento de "conflicto resuelto".
5. **El mapa no es el que piden los requisitos** (H-20). Hoy es un mapa de calor de puntos servido por el API de escritura. El SRS pide zonas con daño consolidado (RF-ED-11), desde un servicio de consulta con caché de geometrías (AD-17, AD-34). Además, los activos no tienen municipio ni zona.
6. **No hay seguridad real** (H-40). No hay gateway ni autenticación: la identidad viaja en encabezados que cualquiera puede falsificar. La jurisdicción territorial solo compara el "nivel" de la autoridad, no su territorio.
7. **La mensajería no cumple AD-02 ni QA-04** (H-30 a H-34):
   - No hay sobre canónico.
   - Las colas no están acotadas y no hay cola de mensajes muertos (DLQ).
   - Un mensaje malformado se reencola para siempre.
   - Los eventos se pierden en silencio si no hay una cola enlazada.
8. **Faltan las integraciones con SE-6 y SE-7** (H-50). Son reglas de negocio del diseño de Reconstrucción, y sin ellas no se demuestra QA-01, CON-3, AD-05, AD-14, AD-36 ni AD-37.
9. **La disponibilidad no llega a lo que exige el SAD** (H-60 a H-64):
   - Danos corre con 1 instancia y guarda las evidencias en el disco local, así que no puede tener 2 réplicas (AD-24).
   - La consulta del programa tiene 1 réplica.
   - RabbitMQ tiene 1 nodo.
   - No hay segunda región ni conmutación (QA-03 es una hoja (A,A)).
10. **Las arquitecturas internas no coinciden** (H-12). Reconstrucción usa puertos y adaptadores. En Danos, el dominio depende directamente del `DbContext`, lo que viola AD-08.

**Esfuerzo estimado para cumplir todo:** entre 70 y 100 días-persona (sección 9). Solo el servicio mínimo de Gestión de Desastres, con su lado de consulta, cuesta entre 10 y 15 días-persona (sección 10).

---

## 2. Alcance que se debe cumplir

### 2.1 Los mínimos de `A_IMPLEMENTAR.md` y su respaldo en los documentos

| Mínimo | Qué es en los documentos | Requisitos | Decisiones del SAD que aplican |
|---|---|---|---|
| **Móvil (recolección, caracterización de un desastre)** | Cliente móvil del evaluador. FP-06, CU-3 del SAD | RF-ED-01, 02, 03, 06, 07, 12, 13. QA-14 | AD-08, 29, 31, 32, 33, 43, 44, 45. AD-04 y AD-22 (todo entra por el gateway) |
| **Reconstrucción física** | Módulo 6. FP-08, escenario E | RF-RC-01 a 09. QA-13, QA-15. Diseño RC (CU-01 a CU-11) | AD-40, 41, 42, 15, 16, 36, 37, 05, 14, 21 |
| **Mapa/visualización de desastres** | Consulta (FP-02: `/v1/consulta/mapa`, `/evolucion`) y Consulta de Daños (`/v1/consulta-danos/mapa`, consolidado y evolución) | RF-ED-10, 11, 15. RF-GD-17, 18. QA-05, QA-06. Enunciado §4.2 (Figura 1: barrios y zonas) | AD-15, 16, 17, 18, 19, 34 |
| **Evaluación de daños** | Módulo 2 completo | RF-ED-01 a 15. QA-14, QA-10 | AD-28 a 34, 43 a 45 |

**Supuesto sobre "caracterización de un desastre".**
- El enunciado (§4.1) dice que el desastre lo registra un usuario autorizado o lo crea un sistema externo de alerta. No lo dice para el móvil.
- Por eso interpreto que el móvil **caracteriza el impacto** del desastre: tipo de activo, nivel 1 a 5, ubicación y evidencias, asociados al desastre.
- El registro del desastre se hace en la consola, dentro del servicio mínimo de Gestión de Desastres (DA-10).

**Sobre "otra plataforma".** El enunciado no dice que el desastre lo registre otra plataforma:
- §4.1 define Gestión de Desastres como "uno de los componentes centrales de la plataforma".
- Lo externo es SE-1 (alertas), que *alimenta* ese módulo.
- No es un mínimo, pero Reconstrucción (AD-40, EV-06) y Evaluación de Daños (RF-ED-06, desastres activos en la descarga) dependen de él.
- **Decidido por usted:** servicio mínimo que respete por completo lo que el diseño define para lo que se implemente.

### 2.2 Decisiones que tomó usted en esta sesión

| Tema | Decisión |
|---|---|
| Gestión de Desastres | Servicio mínimo, fiel al diseño en lo que se implemente |
| Frontend | Una sola consola Angular (la "Consola de coordinación" del SAD) |
| Gateway e identidad | YARP + Keycloak |
| Repos | Monorepo en `proyectoparte2`. La app Android sigue en su repo `danos`, con buenas prácticas y con cambios |
| Regiones | Simuladas en Kubernetes: dos namespaces, base de datos replicada entre regiones, bus de 3 nodos y enrutador global |
| SE-6 y SE-7 | Adaptadores reales contra simuladores que se pueden "caer" |
| Zonas del mapa | PostGIS + Redis |
| Transversales | Auditoría (AD-21). No se incluyen Notificación ni observabilidad |
| Documentos | No se modifican. Las correcciones van en `ENMIENDAS_DOCUMENTOS.md` |

### 2.3 Fuera de alcance

Es decir, lo que se declara como límite, con su argumento:

| Elemento | Razón |
|---|---|
| Notificación (AD-13, FP-05), Consolidación (AD-10 a 12, FP-04), SE-1 automático (FP-01) | No son mínimos y no los eligió. Las colas y los eventos quedan definidos para agregarlos después sin modificar los existentes (QA-09, Cuadro 8 del SAD) |
| Recursos (FP-07), Personas Afectadas (FP-10), Coordinación (FP-09) y Portal público | No son mínimos. El gateway reserva la clase de tráfico "portal" para AD-19 |
| SE-1 a SE-5 y SE-8 | Solo se integran SE-6 y SE-7 (Reconstrucción) y SE-2 (capas GIS, necesario para las zonas, DA-07) |

---

## 3. Inventario actual (resumen)

| Pieza | Repo | Estado |
|---|---|---|
| Reconstrucción, rol escritura | proyectoparte2 | Completo por dentro: 11 CU, outbox, relevo y 43 pruebas |
| Consulta del Programa, rol consulta | proyectoparte2 | Proyección y consolidado |
| Consola Angular | proyectoparte2 | Solo Reconstrucción. El perfil se elige con un selector y se envía en encabezados |
| PostgreSQL con réplica en streaming, RabbitMQ (1 nodo) y MinIO | proyectoparte2 | Compose y k8s, con una sola región |
| API Evaluación de Daños | danos | Descarga, sincronización, evidencias, mapa de calor y lectura de conflictos |
| Simulador de QA-14 | danos | Lote de 120 evaluaciones con 5 conflictos |
| App Android | danos | FP-06 completo, sin login |
| Diseño 4+1 de Danos (LaTeX) | danos | Documento propio del prototipo |

---

## 4. Hallazgos

### 4.1 Integración entre módulos y flujos de punta a punta

**H-01 · Crítica · Integración.** Los módulos no están conectados.
- **Evidencia:**
  - `danos/api/src/Danos.Api/Dominio/Entidades.cs:124`: "Outbox: eventos EV-09 y EV-10 para cuando exista el bus. Por ahora solo se guardan."
  - `reconstruccion/db/03-datos-prueba.sql:1-2`: "En la plataforma completa estos datos llegan como eventos [...] esos módulos todavía no están implementados."
  - Danos no tiene cliente de RabbitMQ, y Reconstrucción no tiene consumidores de eventos ajenos.
- **Impacto:** el escenario E del enunciado ("los reportes de daños son utilizados para crear proyectos") no se puede ejecutar. AD-02 solo se cumple dentro de Reconstrucción.
- **Solución:** DA-01, DA-02, DA-03 y F-1 a F-4.

**H-02 · Crítica · Flujo incompleto.** No existe la fuente de desastres ni de sus estados.
- **Evidencia:**
  - Danos siembra 3 desastres sin estado (`Semilla.cs:15-17`; `Entidades.cs`: `Desastre {Id, Nombre, Tipo}`).
  - Reconstrucción siembra 2 desastres distintos con un estado en texto (`03-datos-prueba.sql`).
  - Los IDs no coinciden (`d0000000-…` frente a `1111…`).
- **Impacto:**
  - AD-40 (disparador EV-06) es inalcanzable.
  - La descarga del móvil manda todos los desastres, incluidos los cerrados (`ServicioDescarga.cs`), pero el SAD dice "desastres activos".
  - No se puede aplicar la regla del Diseño RC "el desastre solo puede cerrarse cuando todos sus proyectos están en un estado final".
- **Solución:** DA-10 (servicio mínimo de Gestión de Desastres).

**H-03 · Crítica · Inconsistencia del SAD (flujo incompleto en el diseño).** EV-09 no trae los datos que necesita su consumidor.
- **Evidencia:** según el Cuadro 16, EV-09 tiene `idEvaluacion, idDispositivo, instanteLocal, instanteServidor, enConflicto, versionesConservadas` y su consumidor es Consulta de Daños. Sin embargo, Consulta de Daños debe construir el mapa y el consolidado "a partir de proyecciones alimentadas por eventos" (AD-34).
- **Impacto:** con ese esquema, Consulta de Daños no conoce el nivel, el tipo, la ubicación ni el desastre de la evaluación. Tendría que leer el almacén de Evaluación de Daños, lo que AD-01 prohíbe.
- **Solución:** extensión aditiva de EV-09 (E-03) y DA-08.

**H-04 · Crítica · Inconsistencia entre documentos.** Hay dos versiones de cómo Reconstrucción obtiene sus candidatas.
- **Evidencia:**
  - SAD Cuadro 15: `GET /v1/reconstruccion/candidatas` "Usa el microservicio de Consulta de Daños", y el Cuadro 16 no lista a Reconstrucción como consumidor de EV-08.
  - El Diseño RC (Figura 1) dice "Evaluación de Daños avisa cuando una evaluación queda validada".
  - El código usa copias locales (`ICopiasLocales`).
  - Además, EV-08 no trae `idActivo` ni municipio, y Reconstrucción los necesita: un proyecto por activo, y alcance municipal o regional (`Servicios.cs:66-86`).
- **Solución:** DA-01 (Reconstrucción consume EV-08 en copia local) y E-01, E-02.

**H-05 · Alta · Flujo incompleto en el diseño.** Faltan eventos para tres flujos que el SAD sí describe.
- **SE-7 entrada:** el Cuadro 14 dice "Entrada de avance y contratista", y el Diseño RC agrega que el avance externo "guarda su procedencia". El Cuadro 16 no tiene ningún evento para eso.
- **Resolución de conflictos:** `POST /v1/conflictos/{id}/resolucion` cambia la evaluación vigente, pero no hay evento. Consulta de Daños no se entera y Auditoría no registra la decisión humana, aunque el ATAM (QA-14, EA-11) exige "auditoría de la decisión humana".
- **Asociación de una evaluación preliminar a un desastre** (`/asociacion`, AD-29): tampoco hay evento. El mapa por desastre nunca la mostraría.
- **Solución:** E-04, E-05 y E-06.

**H-06 · Alta · Flujo incompleto.** No se aplica la regla de cierre del desastre.
- **Evidencia:** el Diseño RC dice "El desastre solo puede cerrarse cuando todos sus proyectos están en un estado final". En el SAD, Gestión de Desastres no consume EV-18 ni EV-20. Reconstrucción calcula `TodosEnEstadoFinal` (`Dtos.cs:31`) solo para mostrarlo.
- **Solución:** DA-10, con su contador local de proyectos no finales, y E-07.

**H-07 · Alta · Inconsistencia de contrato.** Cada módulo codifica los valores de dominio a su manera.
- **Evidencia:**
  - Nivel de daño: `SIN_DANO, LEVE…` en Danos (`Enums.cs`) y `SinDano, Leve…` en Reconstrucción (`01-reconstruccion.sql:21`).
  - Estado del desastre: `EN_RECONSTRUCCION` como texto libre.
  - Fechas: `DateTime` en Danos y `DateTimeOffset` en Reconstrucción.
- **Impacto:** sin un esquema canónico, cada consumidor traduce por su cuenta. Eso es justo lo que AD-09 busca evitar.
- **Solución:** DA-02 (sobre canónico y enumeraciones canónicas en `comun`) y E-08.

**H-08 · Media · Inconsistencia de rutas.** El código no sigue el catálogo de APIs, y una ruta choca con la de otro servicio.

| SAD (Cuadro 15) | Código | Problema |
|---|---|---|
| `POST /v1/proyectos:lote` | `POST /v1/proyectos/lote` | Nombre distinto |
| `POST /v1/proyectos/{id}/avance` | `/avances` | Nombre distinto |
| No existe | `PATCH /v1/proyectos/{id}/responsable`, `POST …/documentos`, `GET /v1/proyectos` | Faltan en el SAD |
| `GET /v1/desastres/{id}` es de Gestión de Desastres | `GET /v1/desastres` en Reconstrucción (`ProyectosController.cs:14`) | **Colisión** de rutas en el gateway |
| `GET /v1/consulta-danos/mapa` | `GET /v1/mapa-calor` en el API de escritura | Mal servicio y mal nombre |
| `POST /v1/evaluaciones`, `PATCH /v1/evaluaciones/{id}`, `/validacion`, `/asociacion`, `/v1/conflictos/{id}/resolucion`, `GET/POST/PATCH /v1/catalogos/tipos-activo` | No existen | Faltan |
| No existe | `GET /v1/evaluaciones` (por dispositivo, para QA-14) | Falta en el SAD |

- **Solución:** DA-12 (registro de rutas en el gateway) y E-09.

### 4.2 Evaluación de Daños (API de Danos)

**H-10 · Crítica · Flujo incompleto.** No existe la validación de evaluaciones (RF-ED-08) ni EV-08.
- **Evidencia:** el estado `VALIDADA` existe (`Enums.cs:20`), pero no hay ninguna ruta ni servicio que lo asigne. No se emite EV-08 en ninguna parte.
- **Impacto:** se cae la cadena completa: evaluación, validación, EV-08 y luego candidatas y proyecto. Tampoco se puede calcular el consolidado de RF-ED-10, que se basa en evaluaciones validadas.
- **Solución:** DA-09 y F-2.

**H-11 · Alta · Flujo incompleto.** La revisión de conflictos y duplicados solo permite leer.
- **Evidencia:** solo existe `GET /v1/conflictos` (`ServicioRevision.cs`). Faltan la resolución (AD-32: "decide un usuario autorizado") y la fusión o el rechazo de duplicados (RF-ED-09, AD-33).
- **Impacto:** la bandeja crece sin salida. Además, el ATAM R-08 pide un umbral de tamaño y antigüedad con alarma, y eso no existe.
- **Solución:** DA-09.

**H-12 · Alta · Mala implementación de AD-08.** El dominio depende de la infraestructura sin pasar por puertos.
- **Evidencia:** `ServicioDescarga(DanosDbContext db)`, `ServicioMapaCalor(DanosDbContext db)`, `ServicioRevision(DanosDbContext db)` y `ServicioEvidencias(DanosDbContext db, AlmacenArchivos …)`, todos en `Dominio/`.
- **Qué dice el SAD (§3.5):** "El dominio usa la infraestructura por medio de puertos, que son interfaces declaradas en el dominio".
- **Impacto:** el dominio no se puede probar sin la base de datos. Además, el módulo no sigue la misma plantilla que Reconstrucción.
- **Solución:** DA-05.

**H-13 · Alta · Mala decisión.** Las evidencias se guardan en el disco local del contenedor.
- **Evidencia:** `AlmacenArchivos.cs:12` (`Path.Combine(ContentRootPath, Ruta)`) y el volumen `evidencias` montado en un solo contenedor.
- **Impacto:**
  - Con las 2 réplicas que exige AD-24, una evidencia subida a la réplica A no existe en la B.
  - El SAD (vista física) define un "Almacén de archivos" como entorno separado.
  - Reconstrucción ya usa MinIO, así que hay dos soluciones distintas para el mismo problema.
- **Solución:** DA-06.

**H-14 · Alta · Seguridad e integridad.** Danos se conecta a la base de datos como superusuario.
- **Evidencia:** `appsettings.json` y `docker-compose.yml` usan `Username=postgres`. El historial de solo inserción se protege únicamente en `DanosDbContext.RechazarCambiosAlHistorial()`.
- **Impacto:** cualquier SQL directo o error de código puede reescribir el historial. Reconstrucción sí lo garantiza con `GRANT SELECT, INSERT` (`01-reconstruccion.sql`). AD-30 reutiliza AD-20, y QA-13 pide un rastro inmutable.
- **Solución:** DT-02 (un rol con mínimo privilegio por servicio y migraciones ejecutadas con el rol dueño).

**H-15 · Media · Despliegue.** Las migraciones y la siembra solo corren en Development.
- **Evidencia:** `Program.cs:49-51`. `Semilla.EjecutarAsync`, que incluye `MigrateAsync`, solo se ejecuta si `IsDevelopment()`.
- **Impacto:** en cualquier otro entorno el servicio arranca sin esquema.
- **Solución:** DT-19 (un Job de migración separado).

**H-16 · Media · Escalabilidad.** La descarga manda todos los activos y todos los desastres.
- **Evidencia:** `ServicioDescarga.cs` no filtra activos, a pesar del comentario "los activos son los mismos para todos".
- **Qué dice el SAD (Cuadro 15):** "desastres activos y activos de la zona con su versión".
- **Impacto:** con decenas de miles de activos, la descarga previa se vuelve inviable en campo (QA-14 y el escenario D).
- **Solución:** DA-19 (filtro por desastre y zona, más descarga incremental por versión).

**H-17 · Media · Funcionalidad faltante.** No existen el registro con conexión, la actualización y la administración del catálogo.
- **Evidencia:** no hay `POST /v1/evaluaciones` ni `PATCH /v1/evaluaciones/{id}` (RF-ED-07 desde la consola). Tampoco hay rutas de administración de tipos de activo y criterios (RF-ED-04 y 05, AD-28, QA-10: "0 redespliegues"). El catálogo solo se siembra.
- **Solución:** DA-18 y F-2.

**H-18 · Baja · Rendimiento.** Hay consultas N+1 en la revisión.
- **Evidencia:** `ServicioRevision.ConflictosAsync` hace 2 o 3 consultas por cada conflicto.
- **Solución:** reescribir con un join cuando se mueva a puertos (F-2).

**H-19 · Media · Modelo de datos.** El activo no tiene municipio ni zona, y siempre es un punto.
- **Evidencia:** `Activo {Lat, Lon, Referencia}`.
- **Impacto:**
  - Sin municipio no hay consolidado "por zona" (RF-ED-10) ni mapa por zonas (RF-ED-11).
  - Reconstrucción no puede decidir la jurisdicción (Municipal o Regional).
  - Los activos lineales que nombra el enunciado (carreteras, redes de agua y eléctricas) no se pueden representar como punto.
- **Solución:** DA-07 y P-03.

### 4.3 Mapa y visualización

**H-20 · Crítica · No cumple el requisito.** El mapa no es de zonas y está en el servicio equivocado.
- **Evidencia:** `ServicioMapaCalor.cs` calcula un punto por activo con un peso, y lo recalcula en cada petición con un `GROUP BY` sobre todas las versiones. Lo sirve el API de escritura (el propio código lo declara en `MapaCalorController.cs:6-8`).
- **Qué exigen los documentos:**
  - RF-ED-11: "zonas afectadas y su nivel de daño consolidado".
  - Enunciado §4.2, Figura 1: barrios y zonas.
  - AD-34: Consulta de Daños con modelo de lectura propio.
  - AD-17: geometría precalculada en caché.
  - QA-06: menos de 2 s con 10 000 usuarios.
- **Impacto:** la carga de consulta cae sobre el servicio que recibe la sincronización. Eso contradice el párrafo de CU-3 en el SAD: "La carga de sincronización no afecta la coordinación, porque el camino de escritura de evaluaciones está separado del camino de consulta".
- **Solución:** DA-08, F-3 y F-6.

**H-21 · Alta.** El mapa mezcla evaluaciones validadas y no validadas sin distinguirlas.
- **Evidencia:** el mapa usa la evaluación vigente de cada activo sin mirar su estado de validación. RF-ED-10 pide un consolidado "a partir de las evaluaciones validadas".
- **Solución:** DA-08. El mapa muestra las vigentes y marca cuáles están validadas, y el consolidado de prioridades usa solo las validadas.

**H-22 · Alta · CON-6 y AD-16.** Ninguna consulta devuelve la marca de tiempo del dato.
- **Evidencia:** `ConsolidadoDto` (`Dtos.cs:28-31`) no tiene instante. El mapa de Danos sí trae `instanteDato`.
- **Impacto:** el usuario no distingue un dato vigente de uno conservado durante un fallo. Esto es lo que compensa el costo de la consistencia eventual (AD-15).
- **Solución:** DA-08, con el campo `datoAl` en toda respuesta de consulta.

**H-23 · Alta.** No hay visualización del desastre ni de su evolución.
- **Evidencia:** no existen `/v1/consulta/mapa` ni `/v1/consulta/desastres/{id}/evolucion` (RF-GD-17 y 18, FP-02), ni la evolución de los daños de RF-ED-15 (`/v1/consulta-danos/activos/{id}/evolucion`).
- **Solución:** DA-10 (el lado de consulta de Gestión de Desastres) y DA-08.

**H-24 · Media · Inconsistencia con el SAD.** El mapa se sirve en un segundo frontend, una página Razor.
- **Qué dice el SAD (Figura 1):** una sola "Consola de coordinación".
- **Solución:** **decidido por usted**, una consola Angular (DA-13).

### 4.4 Reconstrucción

**H-30 · Alta · AD-09 y AD-11.** Los eventos no usan el sobre canónico.
- **Evidencia:** en `Rabbit.cs:105-114` solo viajan `MessageId` y `Type` en las propiedades AMQP, y el cuerpo es `EventoProyecto` (`Eventos.cs:8-11`).
- **Qué dice el SAD (§5.2):** el sobre tiene `idEvento, tipo, version, instanteEmision, productor, idCorrelacion, claveIdempotencia, procedencia`.
- **Solución:** DA-02.

**H-31 · Alta.** EV-18, EV-19 y EV-20 no coinciden con el catálogo.
- **Evidencia:**
  - **EV-18** debe traer `idsEvaluacionesOrigen, fuenteFinanciacion, responsable, cronograma`. `EventoProyecto` no los tiene, así que el adaptador de SE-7 no puede enviar el proyecto completo.
  - **EV-19** ("Presupuesto cambiado"): el código lo convierte en `asignacion_cambiada` y le suma el responsable y el cronograma.
  - **EV-20**: el SAD lo hace consumir por SE-7, pero no por SE-6. Sin embargo, el diseño dice "presupuesto **y costos** al sistema financiero".
- **Solución:** DA-02 y E-10.

**H-32 · Alta · QA-11 y AD-22.** La jurisdicción territorial está incompleta, y la autoridad nacional no puede aprobar nada.
- **Evidencia:** `ProyectoReconstruccion.cs:208-212` solo compara `actor.Ambito` (Local o Regional) con el alcance. Por lo tanto:
  - La alcaldía de Armenia puede aprobar un proyecto de Calarcá.
  - Con `Ambito.Nacional`, que existe en `Tipos.cs:12`, nunca coincide, así que la autoridad nacional no puede aprobar ningún proyecto.
- **Solución:** DA-14 y P-02.

**H-33 · Alta · Seguridad.** El responsable se identifica comparando un texto que se puede falsificar.
- **Evidencia:** `ProyectoReconstruccion.cs:330` (`actor.Usuario != Responsable`), con el usuario tomado del encabezado `X-Usuario`. Además, `AsignarResponsable` no verifica que el responsable exista ni que tenga el rol Responsable.
- **Solución:** DA-15 (identidad desde el JWT, `sub`) y validación del responsable contra el directorio de Keycloak.

**H-34 · Media.** El contratista solo se registra a mano, y falta la procedencia del costo.
- **Evidencia:** `CuerpoTransicion.Contratista` (`Solicitudes.cs:12`) existe, pero nada recibe el contratista desde SE-7, a pesar de que el Diseño RC dice "registrado por el responsable o recibido del sistema de proyectos externo". `registros_costo` no tiene `procedencia`, mientras que `registros_avance` sí.
- **Solución:** DA-11 y F-5.

**H-35 · Media · Despliegue.** El esquema se crea con scripts de inicio que solo corren la primera vez.
- **Evidencia:** `db/*.sql` se monta en `docker-entrypoint-initdb.d`. `k8s/05-esquema.yaml` es una copia que se genera con un script.
- **Impacto:** no hay forma de evolucionar el esquema de una base ya creada, y hay que mantener dos copias.
- **Solución:** DT-19.

**H-36 · Baja.** Si no se configura el rol, el servicio arranca con ambos roles (`todo`).
- **Evidencia:** `Program.cs:10`. Si falta la variable, un pod de escritura levanta también el consumidor de la proyección.
- **Solución:** que falle al arrancar si el rol no viene definido, salvo en Development.

### 4.5 Mensajería

**H-40 · Crítica · QA-04 ("0 eventos perdidos").** El relevo descarta los eventos si no hay una cola enlazada.
- **Evidencia:** `Rabbit.cs:113` publica con `mandatory: false` y sin un *alternate exchange*. Un evento publicado antes de que exista la cola de un consumidor nuevo se descarta, y el relevo lo marca como publicado.
- **Impacto:** cuando se agregue Auditoría, por ejemplo, perderá todo lo anterior a su arranque. Así se materializa ATAM R-02.
- **Solución:** DA-03 (topología declarada al desplegar, `mandatory` con alternate exchange, y reenvío desde la bandeja).

**H-41 · Alta · AD-02 ("colas acotadas por consumidor").** Las colas no tienen límite.
- **Evidencia:** `Rabbit.cs:178` declara la cola solo con `x-queue-type: quorum`, sin `x-max-length` ni política de desborde.
- **Solución:** DA-03.

**H-42 · Alta · Resiliencia.** Un mensaje malformado se reprocesa para siempre.
- **Evidencia:** `Rabbit.cs:194-198`: ante cualquier excepción hace `BasicNack(requeue: true)` cada 2 s. Por ejemplo, `Enum.Parse` falla con un estado desconocido en `Consultas.cs` (`ActualizarProyeccionPrograma`).
- **Impacto:** el mensaje bloquea la cola, con `prefetch 20` y una cola quorum que reentrega, y llena el log. No hay cola de mensajes muertos (DLQ).
- **Solución:** DA-03 (límite de entregas con `x-delivery-limit` de las colas quorum, DLQ y alarma).

**H-43 · Alta · AD-02.** El bus tiene 1 nodo con colas quorum.
- **Evidencia:** `k8s/10-datos.yaml` (StatefulSet de RabbitMQ con `replicas: 1`). El README lo declara.
- **Impacto:** una cola quorum en un solo nodo no replica nada. El SAD pide 3 nodos por región.
- **Solución:** DT-14.

**H-44 · Media · Inconsistencia.** Cada módulo usa un exchange y una convención de nombres propios.
- **Evidencia:** Reconstrucción usa `reconstruccion.eventos` con routing key `proyecto.*`. Danos usa el tipo `"EV-09"` y no tiene bus.
- **Solución:** DA-02 (un exchange de plataforma y claves `evNN.dominio.hecho`).

### 4.6 Seguridad

**H-50 · Crítica · AD-04, AD-22, QA-11 y la estrategia de seguridad (§7.1).** No hay gateway, autenticación ni cifrado en tránsito.
- **Evidencia:** la identidad viaja en `X-Usuario`, `X-Rol` y `X-Ambito` en Reconstrucción (`ControladorBase.cs`), y en `X-Usuario`, `X-Organizacion` y `X-Dispositivo` en Danos (`FiltroEncabezados.cs`). Todo es HTTP sin TLS (`Program.cs` de Danos). Además, la consola elige el perfil con un selector.
- **Impacto:** cualquiera falsifica la identidad. QA-11 (100 % de rechazos a un organismo no responsable) no se puede verificar.
- **Solución:** **decidido por usted**, YARP + Keycloak (DA-15, DT-07, DT-08).

**H-51 · Alta · Secretos.** Las credenciales están fijas en el código y son las de fábrica.
- **Evidencia:** `guest/guest` en RabbitMQ, `minioadmin/minioadmin` en `k8s/00-base.yaml:16-17`, y las contraseñas en `docker-compose.yml` y en `appsettings.json`.
- **Solución:** Secrets de Kubernetes generados por un script local, usuarios del bus por servicio con permisos por vhost y prefijo, y un `.env` fuera del repo para compose.

**H-52 · Alta · QA-13.** No hay auditoría centralizada.
- **Evidencia:** nadie consume los eventos para la auditoría. El rastro de "quién, qué, cuándo, sobre qué y con qué resultado" (enunciado, sección Trazabilidad) está repartido en los historiales de cada servicio.
- **Solución:** **decidido por usted**, Auditoría (DA-17).

### 4.7 Disponibilidad, escalabilidad y despliegue

**H-60 · Crítica · QA-03 (hoja A,A) y AD-26, AD-27.** No hay segunda región ni conmutación.
- **Evidencia:** `db-replica` es una réplica en streaming en la misma máquina, sin promoción ni enrutador. El README lo declara: "La segunda ubicación es simulada".
- **Solución:** **decidido por usted**, simulación en Kubernetes (DA-16, DT-14).

**H-61 · Alta · AD-24.** Hay servicios con una sola réplica.
- **Evidencia:** la API de Danos tiene 1 réplica. En Reconstrucción, el Deployment `consulta` tiene `replicas: 1` y el HPA `minReplicas: 1` (`k8s/20-servicios.yaml:52, 93`). El servicio `consulta` de compose también tiene 1.
- **Qué dice el SAD:** al menos 2 réplicas en los servicios de escritura (AD-24) y entre 2 y N en los de consulta (AD-18).
- **Solución:** DA-16.

**H-62 · Media.** Los sondeos de salud no verifican las dependencias.
- **Evidencia:** `/salud` responde "ok" sin revisar la base de datos ni el bus.
- **Impacto:** el sondeo de AD-24 ("deja de recibir tráfico en 30 s o menos") no detecta una réplica que perdió la conexión a la base de datos.
- **Solución:** `/salud/vivo` (liveness) y `/salud/listo` (readiness, que revisa la base de datos y el bus), con `AspNetCore.HealthChecks`.

**H-63 · Media · AD-01.** Las dos bases de Reconstrucción comparten una instancia de PostgreSQL.
- **Evidencia:** `reconstruccion` y `programa` viven en el mismo servidor `db`.
- **Impacto:** el aislamiento lógico se cumple, con credenciales propias. Pero el fallo del servidor tumba la escritura y la consulta a la vez, y QA-04 pide contener el fallo.
- **Solución:** DA-16. En Kubernetes, un clúster CloudNativePG por servicio. En compose se acepta compartir instancia y se declara.

**H-64 · Media · AD-25.** Los adaptadores no son efímeros, porque no existen.
- **Solución:** DT-10 (KEDA).

### 4.8 Cliente móvil (repo `danos`)

**H-70 · Alta.** La app no tiene identidad real.
- **Evidencia:** la app no tiene login, y el usuario y la organización se escriben a mano en "Ingreso".
- **Solución:** DT-13 (AppAuth con PKCE y `offline_access`).

**H-71 · Media.** La app depende de un contrato que va a cambiar.
- **Detalle:** en la descarga aparecen el municipio y la zona, los desastres solo activos y la descarga incremental. En la sincronización, el token.
- **Solución:** OpenAPI publicado por el monorepo y la prueba de contrato existente (`ContratoServidorTest.kt`) apuntada al gateway.

**H-72 · Baja.** La app no está lista para producción.
- **Evidencia:** `applicationId = "com.example.danos"` y el APK de `entrega/` es de depuración, con texto plano permitido en `debug/network_security_config.xml`.
- **Solución:** un `applicationId` propio y una variante `release` con TLS obligatorio y firma.

### 4.9 Pruebas y verificación

**H-80 · Alta · CON-7.** No hay forma de verificar las medidas numéricas.
- **Detalle:** solo QA-14 tiene un verificador (el simulador de Danos). QA-01 a QA-07 no tienen ninguna prueba que produzca su medida: latencia p95, RTO y RPO, cero eventos perdidos y escalado a 10 000 usuarios.
- **Impacto:** las decisiones quedan sin comprobar ("una decisión solo puede verificarse contra una medida", SRS sobre CON-7).
- **Solución:** DT-16 y F-11.

**H-81 · Media.** No hay pruebas de integración contra infraestructura real.
- **Detalle:** las pruebas de Reconstrucción usan puertos en memoria, así que el relevo, el consumidor y el SQL con permisos no tienen prueba.
- **Solución:** Testcontainers (DT-16).

---

## 5. Flujos de punta a punta objetivo

Todos los flujos entran por el gateway (identidad validada) y publican por la bandeja de salida (AD-45, aplicada a todos los productores, DA-04). Todos los eventos llevan el sobre canónico, y Auditoría consume todos los eventos de cambio.

### F-A · Preparación y descarga (CU-3, paso previo)
```
Autoridad (consola) ─POST /v1/desastres──────────► Gestión de Desastres ──EV-03──► bus
                     PUT  /v1/desastres/{id}/zona                         ──EV-05──► bus
Bus ──EV-03/EV-05/EV-06──► Evaluación de Daños (copia local de desastres activos)
Bus ──EV-03..06──► Consulta (proyecciones + caché de geometrías)
Adaptador SE-2 ──EV-21 capa GIS──► Evaluación de Daños y Consulta y Consulta de Daños (PostGIS)
Evaluador (app) ─GET /v1/descarga?desastre=&zona=&desde=──► Evaluación de Daños
```

### F-B · Registro sin conexión y sincronización (FP-06, QA-14)
Es igual al de hoy, con estos cambios:
- Pasa por el gateway con token.
- Evaluación de Daños asigna `codigoMunicipio` al activo nuevo por punto en polígono (DA-07).
- Publica EV-09 extendido (E-03) y EV-10.
- Consulta de Daños proyecta el mapa y el consolidado.

### F-C · Revisión humana (AD-32, AD-33, R-08)
```
Operador (consola) ─GET /v1/conflictos──────────────► Evaluación de Daños
                   ─POST /v1/conflictos/{id}/resolucion (prevalece X | fusionar | rechazar duplicado)
Evaluación de Daños: nueva versión vigente (nada se borra) ──EV-22 conflicto resuelto──► bus
Bus ──EV-22──► Consulta de Daños (actualiza mapa) · Auditoría (decisión humana)
Alarma R-08: Consulta de Daños proyecta tamaño/antigüedad de la bandeja → aviso en la consola
```

### F-D · Validación (RF-ED-08, la pieza que hoy falta)
```
Autoridad/organismo ─POST /v1/evaluaciones/{id}/validacion {VALIDADA|RECHAZADA|PENDIENTE, motivo}
Evaluación de Daños (regla nivel 2: organismo con competencia sobre el desastre) ──EV-08 (extendido E-02)──► bus
Bus ──EV-08──► Consulta de Daños (consolidado RF-ED-10) · Reconstrucción (copia local evaluaciones_validadas) · Auditoría
```

### F-E · De la emergencia a la reconstrucción (escenario E, FP-08, AD-40)
```
Autoridad ─POST /v1/desastres/{id}/transiciones {EN_RECONSTRUCCION}──► Gestión de Desastres (valida perfil RN-2)
Gestión de Desastres ──EV-06──► Reconstrucción (copia local desastres) · Consulta · Auditoría
Autoridad ─GET /v1/reconstruccion/candidatas?desastre=──► Reconstrucción (lee copia local, sin llamada síncrona)
          ─POST /v1/proyectos:lote──► Reconstrucción ──EV-18──► bus
Bus ──EV-18──► Consulta del Programa · cola de salida de SE-7 · Gestión de Desastres (contador de no finales) · Auditoría
Adaptador SE-7 (efímero, KEDA) ─► simulador SE-7 (cortacircuitos, reintento, idempotencia por idProyecto+versión)
```

### F-F · Presupuesto y costos hacia SE-6 (Diseño RC, AD-36)
```
PATCH /v1/proyectos/{id}/presupuesto ──EV-19──► cola de salida SE-6 · Consulta del Programa · Auditoría
POST  /v1/proyectos/{id}/avance (costo) ──EV-20──► cola de salida SE-6 (E-10) · Consulta del Programa · Gestión de Desastres · Auditoría
SE-6 caído: el circuito se abre, la cola acumula; el proyecto queda confirmado (CON-3). Al volver: se entrega la cola en orden de versión.
Adaptador publica EV-23 "estado de integración" (R-10) → la consola muestra "SE-6 no responde desde hh:mm".
```

### F-G · Entrada desde SE-7 (avance y contratista, Cuadro 14)
```
Adaptador SE-7 (consulta periódica, KEDA cron) ─GET cambios desde marca de avance──► simulador SE-7
──EV-24 avance/contratista reportado por SE-7 (E-04)──► Reconstrucción (registra con procedencia "SE-7"; regla de corrección justificada)
Reconstrucción ──EV-20──► Consulta del Programa · Auditoría
```

### F-H · Cierre del desastre (Diseño RC)
```
Autoridad ─POST /v1/desastres/{id}/transiciones {CERRADO}──► Gestión de Desastres
Gestión de Desastres revisa su contador local (EV-18 +1, EV-20 a Finalizado/Cancelado −1): si > 0 → 409 PROYECTOS_ABIERTOS
```

### F-I · Conmutación regional (QA-03, AD-26, AD-27, R-09)
Se describe en DA-16.

---

## 6. Decisiones de arquitectura propuestas

Cada decisión tiene el mismo formato que el Cuadro 18 del SAD (CON-7): driver, alternativa descartada y costo aceptado. "Relación con el SAD" indica si la decisión **implementa** una decisión existente, la **refina** (agrega detalle que el SAD deja abierto) o la **enmienda** (cambia algo que el SAD dice; ver el archivo de enmiendas).

### DA-01 · Reconstrucción obtiene las candidatas de una copia local alimentada por EV-03, EV-06 y EV-08
- **Relación con el SAD:** enmienda el Cuadro 15 (candidatas "usa Consulta de Daños") y el Cuadro 16 (consumidores de EV-08). Ver E-01.
- **Drivers:**
  - **AD-01:** ningún microservicio depende de otro; el SAD §3.5 dice "Ningún microservicio depende de otro".
  - **CON-6:** disponible durante fallos parciales.
  - **QA-04:** el fallo de Consulta de Daños no debe tumbar Reconstrucción.
  - **CON-5:** la clasificación del modo de cada operación.
  - Es coherente con el Diseño RC (Figura 1) y con el código actual.
- **Alternativa descartada:** la llamada síncrona a Consulta de Daños, como dice el SAD. Crea acoplamiento en tiempo de ejecución, y si Consulta de Daños se degrada, la creación de proyectos falla.
- **Costo aceptado:** consistencia eventual. Una evaluación validada aparece como candidata con un retraso menor que 2 s (el mismo objetivo de AD-15). Reconstrucción guarda una copia que no es suya, aceptado por CON-1: "no es dueña de los datos".

### DA-02 · Sobre canónico, enumeraciones canónicas y un solo exchange de plataforma, en el paquete `comun`
- **Relación con el SAD:** implementa AD-09 y §5.2, y refina el nombre del exchange y de las claves.
- **El sobre** (JSON Schema en `comun/esquemas/sobre.v1.json`):
  ```json
  {
    "idEvento": "uuid",
    "tipo": "EV-08",
    "nombre": "evaluacion.validada",
    "version": 1,
    "instanteEmision": "2026-11-16T14:03:22.120Z",
    "productor": "evaluacion-danos",
    "secuenciaProductor": 18342,
    "idCorrelacion": "uuid",
    "claveIdempotencia": "evaluacion:3f…:v4",
    "procedencia": { "fuente": "plataforma", "sistemaOrigen": null, "confianza": "ALTA" },
    "datos": { }
  }
  ```
- **Enumeraciones canónicas:**
  - Nivel de daño: `SIN_DANO, LEVE, MODERADO, GRAVE, DESTRUCCION_TOTAL`.
  - Estado del desastre: `DETECTADO, REGISTRADO, EN_EVALUACION, EN_ATENCION, EN_RECUPERACION, EN_RECONSTRUCCION, CERRADO`, según el enunciado §4.1.
  - Instantes en UTC con ISO-8601 y zona explícita, según el SRS Cuadro 7, atributo 12: "con zona horaria explícita".
- **Exchange y claves:** un exchange de tipo *topic* durable `plataforma.eventos`, con la clave `evNN.<agregado>.<hecho>`, por ejemplo `ev08.evaluacion.validada`. Cada consumidor enlaza sus claves.
- **`secuenciaProductor`** es la mitigación de ATAM R-02: Auditoría detecta huecos. Reutiliza la columna `secuencia` de la bandeja de salida de Reconstrucción.
- **Drivers:** AD-09, QA-09 (un adaptador nuevo solo conoce el sobre), R-02 y R-03 (regla de evolución aditiva: un cambio semántico es una versión nueva del evento, que convive con la anterior).
- **Alternativas descartadas:**
  - **Un exchange por módulo** (lo que hay hoy): obliga a cada consumidor a conocer al productor, y eso rompe la idea de intermediario.
  - **Avro o Protobuf con un registro de esquemas:** agrega un componente de infraestructura sin un beneficio que pida ningún QA. Los sistemas heterogéneos (CON-2) se integran por adaptadores, no por el bus.
- **Costo aceptado:** la validación del esquema se hace en ejecución, igual que el costo aceptado de AD-07.
- **Sobre CloudEvents:** no está en los documentos. El sobre se puede mapear a CloudEvents 1.0 (`id, type, source, time`) si hace falta interoperar con terceros. No se adopta ahora para no cambiar los nombres de campo del SAD.

### DA-03 · Topología del bus declarada al desplegar, colas acotadas, DLQ y entrega garantizada desde la bandeja
- **Relación con el SAD:** implementa AD-02 ("colas acotadas por consumidor") y AD-45, y corrige H-40, H-41 y H-42.
- **Diseño:**
  - **Topología declarada al desplegar.** Un Job de despliegue (`topologia-bus`, Definitions de RabbitMQ) declara el exchange, las colas, los enlaces y las políticas *antes* de que arranque cualquier productor. Las colas existen desde el principio, aunque su consumidor todavía no haya arrancado.
  - **Una cola por consumidor:** `q.reconstruccion`, `q.consulta-danos`, `q.consulta`, `q.consulta-programa`, `q.gestion-desastres`, `q.auditoria`, y `q.salida.se6` y `q.salida.se7` (las colas de salida de AD-36).
  - **Colas quorum** con `x-max-length`, `overflow: reject-publish` (el productor recibe nack y el evento se queda en su bandeja: es acotada y no pierde nada), `x-delivery-limit: 5` y un `dead-letter-exchange` que lleva a `q.<consumidor>.dlq`.
  - **El relevo publica con `mandatory: true`.** Si vuelve un `basic.return` o un nack, no marca el evento como publicado. El exchange tiene un *alternate exchange* que lleva a `q.sin-ruta`, con alarma.
  - **Consumo idempotente** por `claveIdempotencia`, con una tabla `mensajes_procesados` en la base de cada consumidor y escrita en la misma transacción que el efecto. Hoy Reconstrucción usa la versión de la fila, lo cual sirve para las proyecciones pero no para efectos que no tienen versión.
- **Drivers:** QA-04 (0 eventos perdidos; el fallo de un consumidor no detiene al productor), AD-02, AD-45 y R-02.
- **Alternativa descartada:** `overflow: drop-head` (el comportamiento por defecto de `x-max-length`). Descarta eventos en silencio e incumple QA-04.
- **Costo aceptado:** si una cola está llena, el productor deja de avanzar en *ese* evento. Con una sola bandeja, eso frena a los demás consumidores (bloqueo en la cabeza de la fila). **Mitigación:** el relevo publica por lotes y, si un tipo de evento es rechazado, salta al siguiente tipo. Además, el umbral de la cola genera una alarma antes de llenarse.

### DA-04 · Todos los productores usan la bandeja de salida
- **Relación con el SAD:** refina AD-45. En el SAD la bandeja solo aparece en Evaluación de Daños, pero Reconstrucción ya la usa, y la mitigación de R-09 la toma como principio.
- **Diseño:** un componente `comun/Bandeja` con la tabla, el relevo con `FOR UPDATE SKIP LOCKED` (el código actual de `Rabbit.cs:86-120`, que es correcto y se reutiliza) y la asignación de `secuenciaProductor`.
- **Drivers:** QA-04 (0 eventos perdidos), QA-15 (b: "0 cambios aplicados sin registro") y R-09.
- **Alternativa descartada:** publicar después del commit. El SAD ya la descartó en AD-45.
- **Costo aceptado:** la entrega es al menos una vez, así que todo consumidor debe ser idempotente (DA-03).

### DA-05 · Una sola plantilla interna para todos los microservicios
- **Relación con el SAD:** implementa AD-08 y corrige H-12.
- **Diseño:** se toma como plantilla la estructura de Reconstrucción, que ya cumple AD-08 con puertos. Cada servicio tiene:
  - `X.Dominio`: entidades, reglas y puertos, sin paquetes.
  - `X.Aplicacion`: casos de uso, puertos de entrada y de salida, y DTO.
  - `X.Infraestructura`: EF Core, bandeja, consumidores, MinIO y PostGIS.
  - `X.Api`: controladores (la capa de acceso de AD-08), contratos y validación (la capa de presentación del servicio), y autorización de nivel 2.

  **Correspondencia con AD-08:** presentación y acceso son `X.Api`, dominio es `X.Dominio` más `X.Aplicacion`, e infraestructura es `X.Infraestructura`. La dependencia va hacia adentro por medio de puertos, como pide §3.5.
- **Evaluación de Daños** se migra a esta plantilla. Su lógica de sincronización (`ServicioSincronizacion`, la regla de conflicto y el detector de duplicados) se conserva **sin cambiar su comportamiento**, porque es la que verificó QA-14. El simulador y las pruebas de integración actuales son la red de seguridad del refactor.
- **Drivers:** AD-08, QA-10, QA-09 (Cuadro 8: "Se agrega un paquete con las cuatro capas") y la mantenibilidad.
- **Alternativa descartada:** dejar a Danos en un solo proyecto con carpetas. Así el dominio depende de EF Core y las pruebas de dominio necesitan base de datos.
- **Costo aceptado:** un refactor de 3 a 4 días, y más proyectos por servicio.

### DA-06 · Evidencias y documentos en un almacén de objetos, con un bucket por servicio
- **Relación con el SAD:** implementa la vista física ("Almacén de archivos") y AD-24, y corrige H-13.
- **Diseño:** MinIO, con los buckets `evaluacion-danos-evidencias` y `reconstruccion-documentos`, y credenciales por servicio con política de bucket (AD-01: cada uno, dueño de lo suyo). El hash SHA-256 se sigue verificando en el servidor (RF-ED-03). La clave del objeto es `evaluacion/{id}/{idEvidencia}` y es inmutable: una evidencia nueva es otro objeto.
- **Drivers:** AD-24 (2 o más réplicas que comparten el almacén), RF-ED-03 y RF-RC-06.
- **Alternativa descartada:** un volumen compartido de tipo ReadWriteMany. Docker Desktop no lo ofrece de forma fiable, y acopla el ciclo de vida del almacén al del pod.
- **Costo aceptado:** una dependencia más para Evaluación de Daños. Si MinIO cae, la subida de evidencias falla, pero la sincronización de evaluaciones sigue funcionando, porque las evidencias se suben aparte (CU-3, paso 7). Esto ya ocurre hoy.

### DA-07 · Divisiones administrativas por un adaptador de SE-2, y municipio asignado en el servidor
- **Relación con el SAD:** refina AD-03 y el Cuadro 14 (SE-2: "entrada de capas y zonas"), y agrega un evento. Ver E-11 (EV-21) y E-12 (el atributo municipio).
- **Diseño:**
  - **El adaptador SE-2** carga las capas (municipios de Colombia, del Marco Geoestadístico Nacional del DANE, en GeoJSON) y publica **EV-21, "Capa geográfica actualizada"**, con la versión de la capa y la geometría de cada división, simplificada para el mapa.
  - **Evaluación de Daños** guarda la capa en PostGIS (su copia local). Al crear un activo, calcula `codigoMunicipio` (DIVIPOLA) con `ST_Contains`, y para un activo lineal usa `ST_Intersects` con el municipio de mayor longitud. El activo conserva su municipio aunque la capa cambie después: el valor lleva la versión de la capa.
  - **EV-08 y EV-09** llevan `codigoMunicipio` y `codigoDepartamento`.
  - **Consulta de Daños y Consulta** guardan la misma capa para agregar y para servir las geometrías (DA-08).
- **Drivers:**
  - RF-ED-10 y 11 (por zona).
  - La jurisdicción de Reconstrucción (Diseño RC).
  - **CON-3:** no se consulta a SE-2 en el camino crítico. El SAD, en AD-17, ya descartó "consultar a SE-2 en cada petición".
  - **AD-01:** cada servicio tiene su propia copia.
- **Alternativas descartadas:**
  - Que el evaluador elija el municipio: introduce errores humanos y no escala.
  - Que Consulta de Daños asigne la zona: Reconstrucción no la recibiría por EV-08.
  - Una llamada síncrona a un servicio GIS: viola CON-3.
- **Costo aceptado:** tres copias de la capa, unos pocos MB por cada 1 100 municipios simplificados. Como en el AD-28 del SAD, aquí se repite un patrón para mantener el aislamiento.
- **Sobre los barrios:** la Figura 1 del enunciado muestra barrios. **No está en los documentos** qué granularidad exigir. Propongo municipio como zona obligatoria y barrio o vereda como capa opcional cuando exista. Ver P-04.

### DA-08 · Consulta de Daños como microservicio de lectura, con mapa por zona, consolidado, evolución y marca de tiempo
- **Relación con el SAD:** implementa AD-34, AD-16 y AD-17 (la caché aplicada a las geometrías de las zonas de daño), y corrige H-20 a H-23.
- **Proyecciones** en PostGIS, alimentadas por EV-08, EV-09, EV-10, EV-21 y EV-22:
  - `p_evaluacion_vigente`: por activo, con nivel, tipo, municipio, desastre, si está validada y si tiene conflicto pendiente.
  - `p_dano_por_zona`: por desastre, municipio, tipo de activo y nivel, solo con validadas. Es el consolidado de RF-ED-10.
  - `p_historial_activo`: RF-ED-15.
  - `p_bandeja`: el tamaño y la antigüedad del conflicto más viejo, para la alarma de R-08.
- **Rutas:**
  - `GET /v1/consulta-danos/mapa?desastre=&nivelMinimo=&soloValidadas=`: devuelve un GeoJSON con las zonas (coropleta por índice de daño de la zona) y, opcionalmente, los puntos.
  - `GET /v1/consulta-danos/consolidado?desastre=`: el impacto por zona y tipo, más la priorización.
  - `GET /v1/consulta-danos/activos/{id}/evolucion`.
- **Toda respuesta lleva `datoAl`**: el instante del último evento aplicado (AD-16 y CON-6).
- **Caché (Redis):**
  - Las geometrías simplificadas por zona y versión de capa se guardan con la clave `geo:{capa}:{version}:{codigo}`.
  - El GeoJSON del mapa ya armado se guarda con la clave `mapa:{desastre}:{filtros}`, con un TTL corto, y se invalida al aplicar un evento de ese desastre.
  - EV-05 y EV-21 invalidan las geometrías, igual que en AD-17.
  - **Degradación:** si Redis cae, se calcula desde PostGIS (más lento, pero correcto). Si PostGIS de consulta cae, se sirve el último mapa en caché con `degradado: true` (CON-6).
- **Índice de daño de la zona:**
  - **No está en los documentos.** Propongo que sea el promedio de los pesos del mapa de calor actual (0, 0,25, 0,5, 0,75 y 1), ponderado por la cantidad de activos, y que se muestre también la distribución por nivel.
  - **Priorización de RF-ED-10:** se ordenan las zonas por la cantidad de activos de tipo crítico (hospital, escuela, puente, red de agua) en nivel 4 o 5, y después por el índice.
  - Confírmelo en P-05.
- **Drivers:** RF-ED-10, 11 y 15; QA-05, QA-06; AD-15, 16, 17, 18 y 34; y el párrafo del CU-3 sobre separar la sincronización de la consulta.
- **Alternativa descartada:** seguir calculando el mapa en el API de escritura. Es la operación más costosa de la consulta y compite con la sincronización.
- **Costo aceptado:** consistencia eventual menor que 2 s, compensada con `datoAl`. Hay un servicio, una base y una caché más.

### DA-09 · Validación, resolución y fusión en Evaluación de Daños, que es dueña de la bandeja de revisión
- **Relación con el SAD:** implementa RF-ED-08, RF-ED-09, AD-32 y AD-33, y enmienda el Cuadro 16 (EV-10 apunta a la "bandeja de revisión" de Consulta de Daños). Ver E-05 y E-13.
- **Diseño:**
  - **`POST /v1/evaluaciones/{id}/validacion`** recibe `{estado: VALIDADA|RECHAZADA|PENDIENTE, motivo}`. Crea una versión nueva (AD-30), registra quién lo hizo y cuándo (RF-ED-08) y emite EV-08.
    - **Regla de nivel 2:** valida un usuario con rol `organismo` o `autoridad` de una organización con competencia sobre el desastre. La competencia es la lista de organismos participantes del desastre, que llega por EV-27 desde Gestión de Desastres (E-25).
    - **No se puede validar** una evaluación con un conflicto pendiente, ni una evaluación preliminar sin desastre.
  - **`POST /v1/conflictos/{id}/resolucion`** recibe `{decision: PREVALECE_ENTRANTE|PREVALECE_VIGENTE|FUSIONAR|RECHAZAR_DUPLICADO, justificacion}`. **Ninguna versión se elimina**: la decisión crea una versión nueva que apunta a la que prevalece. Emite **EV-22, "Conflicto resuelto"** (E-05).
  - **"Fusionar"** no está definido en los documentos. Propongo que la evaluación que se descarta quede `DUPLICADO_DE = X`, conserve todas sus versiones y sus evidencias se vinculen también a X (E-14).
  - **La bandeja vive en Evaluación de Daños** (lectura y decisión). Consulta de Daños solo proyecta los indicadores.
  - **Alarma de R-08:** los umbrales de tamaño y antigüedad son configurables (por ejemplo, 50 conflictos o 24 h). Si se superan, la consola muestra un aviso y se emite un registro de auditoría.
- **Drivers:** RF-ED-08 y 09, AD-32, AD-33, QA-14 y R-08.
- **Alternativa descartada:** la bandeja en Consulta de Daños, como dice el SAD. Resolver exige leer la versión vigente con consistencia fuerte y escribir en el mismo almacén. Con una proyección eventual, el operador podría decidir sobre una versión que ya no es la vigente.
- **Costo aceptado:** las lecturas de la bandeja cargan a Evaluación de Daños. El volumen es bajo (lo leen operadores, no ciudadanos), así que no afecta a QA-05.

### DA-10 · Gestión de Desastres mínima, completa en lo que abarca
- **Relación con el SAD:** implementa un subconjunto de AD-06, AD-07, AD-20, AD-22 y AD-15 (con una Consulta mínima), y RN-1, RN-2 y RN-3.
- **Qué se incluye, y cómo se cumple todo lo definido para cada parte:**

| Pieza | Documentos | Qué se implementa |
|---|---|---|
| Catálogo de tipos y perfiles de ciclo de vida | AD-07, RN-1, RN-2, QA-10, `GET/POST/PATCH /v1/catalogos/tipos-desastre`, `GET/PUT …/{cod}/ciclo-vida` | Tipos y máquinas de estados **como datos**, editables en ejecución, con validación en ejecución. Los 7 estados del enunciado y transiciones por tipo (por ejemplo, para nivel 1: `REGISTRADO → CERRADO`) |
| Registro manual | RF-GD-02, RF-GD-03 (los 12 atributos del Cuadro 7), RF-GD-04 (validación contra el catálogo), `POST /v1/desastres` | Registro con los 12 atributos. Rechaza un tipo no definido. `origen = MANUAL`. Emite EV-03 |
| Clasificación y reclasificación | RF-GD-05, RF-GD-06 (P), RN-3 (niveles 1 a 4), AD-20, `PATCH /v1/desastres/{id}/severidad` | Fila versionada con valor anterior, nuevo, autor, justificación e instante. Emite EV-04 |
| Transiciones | RF-GD-07, RN-2, `POST /v1/desastres/{id}/transiciones` | Valida contra el perfil del tipo. Emite EV-06. **Agrega la regla de cierre** con el contador de proyectos no finales (F-H y E-07) |
| Delimitación versionada | RF-GD-08, RF-GD-09 (P), FP-03, AD-20, `PUT /v1/desastres/{id}/zona` | Zona como conjunto de municipios y/o un polígono (PostGIS), versionada y nunca sobrescrita. Emite EV-05 |
| Organismos participantes | `/v1/desastres/{id}/organismos`, AD-22 | Organismo responsable y participantes, necesarios para la autorización de nivel 2 en Gestión de Desastres (QA-11) y en Evaluación de Daños (DA-09) |
| Historial | `GET /v1/desastres/{id}/historial` | La evolución de severidad, zona y estado |
| Autorización de nivel 2 | AD-22, QA-11 | Solo el organismo responsable modifica la severidad y la zona |
| Consulta (lectura) | AD-15, 16, 17, 18, FP-02 (P), `/v1/consulta/desastres`, `/{id}`, `/mapa`, `/{id}/evolucion` | Proyecciones desde EV-03 a 06, la caché de geometrías de las zonas del desastre (AD-17, invalidada por EV-05) y `datoAl` |

- **Qué se excluye, sin romper nada (QA-09, Cuadro 8):** SE-1 y FP-01 (registro automático), Consolidación (FP-04), Notificación (FP-05) y la gestión de fuentes (RF-GD-10 y 11). Las colas `q.gestion-desastres` y los eventos EV-01 y EV-02 quedan en el catálogo. Agregarlos después es "agregar un paquete", sin modificar este servicio.
  - **Una salvedad:** el campo `origen` de EV-03 ya distingue manual o externo, así que el contrato no cambia.
- **Drivers:** AD-40 (sin EV-06 no hay reconstrucción), RF-ED-06 (desastre registrado), FP-02 y FP-03 (la visualización y la evolución del desastre, que son parte del mínimo "Mapa/visualización de desastres") y QA-11.
- **Alternativa descartada:** un simulador que solo publique eventos. Usted pidió que lo que se haga respete el diseño, y sin el catálogo ni el ciclo de vida, EV-06 no tendría ninguna regla detrás.
- **Costo aceptado:** de 10 a 15 días-persona (sección 10).

### DA-11 · Adaptadores de SE-6 y SE-7 con el contrato común, contra simuladores
- **Relación con el SAD:** implementa AD-03, 05, 09, 14, 25, 36 y 37. Agrega EV-23 (R-10) y EV-24 (la entrada de SE-7). Ver E-04 y E-15.
- **Diseño:**
  - **`comun/Adaptadores`:** la interfaz `IContratoAdaptador` con las operaciones de entrada `RecuperarDesde(marcaAvance)` y `Traducir(externo) → EventoCanónico`, y la de salida `Entregar(EventoCanónico)`. El bus solo usa esta interfaz (AD-09).
  - **`adaptador-se6` (financiero).** Consume `q.salida.se6` (EV-18, EV-19 y EV-20 con costo) y entrega por lotes (Cuadro 14: "envío por lote"). La idempotencia usa `idProyecto + versionHistorial` (Cuadro 14).
  - **`adaptador-se7` (proyectos).** La salida consume `q.salida.se7` (EV-18 y EV-20). La entrada hace una consulta periódica de avances y contratistas desde su marca de avance y publica EV-24.
  - **Resiliencia:** `Microsoft.Extensions.Http.Resilience` (Polly v8), con timeout por intento, reintento exponencial con jitter (solo para operaciones idempotentes, según el Cuadro 5) y cortacircuitos.
    - **El Cuadro 5** dice que el circuito "se abre tras tres tiempos de espera agotados". El cortacircuitos de Polly mide una proporción en una ventana, así que se configura con `MinimumThroughput = 3`, `FailureRatio = 1.0` y `SamplingDuration = 30 s`. Eso equivale a "3 de 3 fallos en la ventana".
    - **La apertura y el cierre** del circuito publican **EV-23, "Estado de integración cambiado"**, que es la mitigación de R-10. Consulta y Consulta del Programa lo proyectan, y la consola muestra "SE-6 no responde desde hh:mm" en 30 s o menos (QA-01 b).
  - **La marca de avance** se guarda en la base `adaptadores`, con un esquema por adaptador. Los adaptadores efímeros no tienen estado local.
  - **Simuladores** `sim-se6` y `sim-se7`: APIs pequeñas en ASP.NET con `POST /admin/caida {on|off|lento}` para probar QA-01. `sim-se7` genera avances y asigna contratistas con un registro de cambios que se puede consultar por marca de avance.
- **Drivers:** las reglas de negocio del Diseño RC ("se envían al sistema de proyectos externo [...] si no responde, la operación se confirma igual y queda en cola con reintento"), CON-3, QA-01, QA-09 y R-10.
- **Alternativa descartada:**
  - **Recepción por *webhook* en SE-7:** con adaptadores efímeros que escalan a cero (AD-25), un *webhook* necesita un receptor siempre encendido. La consulta periódica está en el Cuadro 14 como patrón de SE-7 y cubre de forma natural la recuperación desde la marca de avance (AD-14).
  - **WireMock:** no modela el estado (el avance cambia con el tiempo) ni la marca de avance.
- **Costo aceptado:** latencia de entrada igual al intervalo de consulta (se propone 30 s). Mantener dos simuladores.

### DA-12 · Registro de rutas en el gateway, con declaración obligatoria de la regla de dominio
- **Relación con el SAD:** implementa AD-04 y la mitigación de R-07. Corrige la colisión de rutas de H-08.
- **Diseño:** el gateway carga un catálogo de rutas (YARP `ReverseProxy:Routes`). Cada ruta lleva metadatos:
  - `servicio`,
  - `roles` (nivel 1),
  - `claseTrafico` (`consola`, `movil` o `portal`),
  - `reglaDominio` (`ninguna` o el nombre de la regla de nivel 2).

  **Al arrancar**, el gateway **rechaza la configuración** si una ruta de modificación (POST, PUT, PATCH o DELETE) no declara `reglaDominio`. Esa es la propuesta textual de R-07.

  **Las rutas se alinean con el Cuadro 15** (E-09), y la ruta `GET /v1/desastres` de Reconstrucción pasa a `GET /v1/reconstruccion/desastres`.
- **Drivers:** AD-04, R-07 y QA-11.
- **Alternativa descartada:** confiar en la disciplina de desarrollo. El ATAM ya la identificó como riesgo.
- **Costo aceptado:** cada ruta nueva exige una entrada en el catálogo.

### DA-13 · Una sola Consola de coordinación en Angular
- **Relación con el SAD:** implementa la Figura 1 y el Cuadro 4. **Decidido por usted.**
- **Diseño:** la consola Angular actual se amplía con módulos de carga diferida por área:
  - `desastres`: registro, severidad, zona y transiciones.
  - `mapa`: Leaflet, con las capas de zonas del desastre, daño por zona y puntos.
  - `evaluaciones`: validación, bandeja de conflictos y catálogo de tipos de activo.
  - `reconstruccion`: lo que ya existe.
  - `auditoria`: consulta del rastro.

  El login es OIDC con PKCE contra Keycloak, y los menús se arman según los roles del token. Se elimina el selector de perfil. La página Razor de Danos desaparece.
- **Drivers:** la vista lógica, AD-19 (la consola tiene prioridad sobre el portal; necesita su propia clase de tráfico) y una sola sesión.
- **Costo aceptado:** migrar el mapa de JavaScript a Angular, con Leaflet como la misma biblioteca.

### DA-14 · Jurisdicción territorial por código de territorio
- **Relación con el SAD:** refina AD-22 y la regla de aprobación del Diseño RC. Corrige H-32.
- **Diseño:** el token trae los claims `ambito` (`LOCAL`, `REGIONAL` o `NACIONAL`) y `territorio` (código DIVIPOLA del municipio o del departamento). El proyecto guarda los municipios de sus evaluaciones de origen (EV-08, DA-07). La regla es:
  - **`Municipal`** (un municipio): lo aprueba la autoridad `LOCAL` de **ese** municipio.
  - **`Regional`** (varios municipios del mismo departamento): lo aprueba la autoridad `REGIONAL` de **ese** departamento.
  - **`Nacional`** (municipios de varios departamentos): lo aprueba la autoridad `NACIONAL`.
    - **Esto no está en los documentos.** El Diseño RC menciona a la autoridad nacional en su contexto, pero no le asigna ninguna aprobación. Ver P-02.
- **Drivers:** QA-11, AD-22 y la regla "Lo aprueba la autoridad con jurisdicción sobre el proyecto".
- **Costo aceptado:** un nuevo valor de alcance (`Nacional`) en el dominio de Reconstrucción.

### DA-15 · Identidad y autorización en dos niveles, con el JWT validado también en cada servicio
- **Relación con el SAD:** implementa AD-04 y AD-22, y la estrategia de seguridad del §7.1.
- **Diseño:**
  - **Keycloak**, con el realm `plataforma`.
    - **Roles de realm:** `autoridad`, `responsable-proyecto`, `auditor`, `evaluador`, `operador`, `organismo` y `administrador`.
    - **Atributos de usuario como claims:** `organizacion`, `ambito` y `territorio`.
    - **Clientes:** `consola` (público, PKCE), `movil` (público, PKCE, `offline_access`) y `gateway` (*audience*).
  - **El gateway** valida el JWT, aplica la regla de nivel 1 (rol y organización contra la ruta, DA-12), limita la tasa y prioriza (DA-16), y **reenvía el mismo JWT**.
  - **Cada servicio vuelve a validar el JWT** (mismo emisor y audiencia) y aplica su regla de nivel 2. **Los servicios no aceptan encabezados `X-*` de identidad.**
  - **Auditoría** registra tanto el rechazo del nivel 1 como el del nivel 2. Según el §7.1, "ambos publican el resultado de la verificación al registro de auditoría".
  - **TLS:** se termina en el enrutador global y en el gateway. Entre servicios, dentro del clúster, se usa HTTP declarado. **No está en los documentos** cómo cifrar el tráfico interno. El §7.1 dice "Toda comunicación se cifra en tránsito", y cumplirlo del todo exige mTLS (por ejemplo, con un *service mesh*). Propongo declararlo como límite del prototipo. Ver P-06.
- **Drivers:** QA-11, AD-04, AD-22 y H-50.
- **Alternativa descartada:** que el gateway traduzca el token a encabezados `X-*` y los servicios confíen en ellos. Cualquier ruta interna que no pase por el gateway quedaría sin protección, y eso no es defensa en profundidad.
- **Costo aceptado:** cada servicio depende del JWKS de Keycloak. Mitigación: se guarda en caché local, de modo que una caída corta de Keycloak no invalida los tokens ya emitidos.

### DA-16 · Disponibilidad, escalado y dos regiones simuladas
- **Relación con el SAD:** implementa AD-18, 19, 24, 26 y 27, y las mitigaciones de R-04 y R-09. **Decidido por usted:** simulación en Kubernetes.
- **Réplicas:**
  - Los servicios de escritura, el gateway y Auditoría tienen 2 o más réplicas, con `PodDisruptionBudget minAvailable: 1` y *readiness* que revisa la base de datos y el bus (AD-24).
  - Los servicios de consulta tienen un HPA entre 2 y N (AD-18).
  - Los adaptadores van de 0 a N con KEDA (AD-25).
- **Priorización y degradación (AD-19):** el limitador del gateway tiene particiones por `claseTrafico`. La consola tiene concurrencia reservada, y el portal y el resto comparten el remanente. Cuando se agota, la consulta responde **con la última respuesta en caché marcada `degradado: true`**, no con un 503 (CON-6).
- **Dos regiones** (namespaces `region-a`, activa, y `region-b`, en espera):
  - **PostgreSQL con CloudNativePG.** En `region-a`, un `Cluster` por servicio con 1 primario y 1 réplica local. En `region-b`, un *replica cluster* de CloudNativePG por cada uno, que recibe el WAL en streaming (asíncrono, según T-02). La alarma salta si el retraso pasa de 30 s (PS-02).
  - **RabbitMQ:** 3 nodos en `region-a` con el Cluster Operator y 1 nodo en `region-b` ("capacidad mínima", AD-26). Los eventos no se replican por el bus, porque la fuente de verdad es la bandeja de cada servicio, que viaja replicada con la base de datos (ver R-09 abajo).
  - **Servicios en `region-b`** con 1 réplica ("capacidad mínima", Cuadro 6), en espera.
  - **Enrutador global** (2 réplicas de HAProxy en un namespace `global`). Sondea `/salud/listo` del gateway de cada región y desvía el tráfico a `region-b` si `region-a` no responde durante N sondeos.
  - **El conmutador** es un Job que se dispara con la alarma del enrutador:
    1. Promueve los *replica clusters* de `region-b`.
    2. Escala `region-b` al tamaño normal.
    3. Activa los relevos de `region-b`.

    **Esto no está en los documentos:** el SAD dice "conmutación automática" pero no define el mecanismo. CloudNativePG no promueve un *replica cluster* por sí solo, así que el conmutador es lo que lo hace automático.
  - **Mitigación de R-09** (eventos en tránsito). Tras la promoción, el relevo de `region-b` **reenvía** los eventos de la bandeja con `publicado_en` dentro de los últimos `RPO + margen`, por ejemplo 2 minutos, aunque figuren como publicados. Los consumidores son idempotentes (DA-03), así que un duplicado no tiene efecto. Esto aplica la propuesta del ATAM ("el productor reenvía el tramo pendiente") sin replicar el bus.
  - **AD-27 (reincorporación):** la `region-a` recuperada se reconstruye como *replica cluster* de la `b` (con `pg_rewind` o un clon nuevo). Solo cuando su retraso es 0 se hace la conmutación inversa, que es manual y planificada.
  - **R-04:** se acepta como límite (T-08). Se mide el RTO real en la prueba de F-10.
- **Drivers:** QA-02, QA-03, QA-04, QA-05, AD-18, 19, 24, 26, 27, R-04 y R-09.
- **Alternativa descartada:**
  - **Dos clústeres kind:** duplica el plano de control y consume mucha más RAM.
  - **Federación o *shovel* del bus entre regiones:** duplica la entrega y no resuelve R-09 mejor que reenviar desde la bandeja.
- **Costo aceptado:** consumo de RAM (sección 9.3, riesgo RG-1). Las dos regiones comparten el mismo nodo físico, así que la prueba demuestra el mecanismo, no la independencia física. Eso se declara.

### DA-17 · Microservicio de Auditoría
- **Relación con el SAD:** implementa AD-21 y la mitigación de R-02. **Decidido por usted.**
- **Diseño:**
  - **Entrada:** consume `q.auditoria`, enlazada a `#` (todos los eventos de cambio), y también los rechazos de autorización (DA-15).
  - **Almacén:** una tabla particionada por mes, `registro(id_evento PK, tipo, productor, secuencia, instante, actor, entidad, id_entidad, accion, resultado, sobre jsonb)`, con `GRANT INSERT, SELECT` y sin UPDATE ni DELETE (AD-41, QA-13: inmutable).
  - **Idempotencia:** por `claveIdempotencia` (AD-21).
  - **Detección de huecos:** guarda `(productor, secuencia)`. Un salto genera una alerta y un registro `HUECO_DETECTADO` (R-02).
  - **Consulta:** `GET /v1/auditoria?entidad=&actor=&desde=&hasta=`, solo para los roles `auditor` y `autoridad`. **No tiene ruta de escritura** (AD-21).
  - **Retención:** al menos 5 años (QA-13), con particiones mensuales que se separan y archivan.
- **Medida de QA-15 (c):** la entrada debe estar disponible en 5 s o menos. Se verifica en F-11.
- **Drivers:** QA-13, QA-15, AD-21 y R-02.
- **Costo aceptado:** un componente más que no debe perder eventos. Se cubre con DA-03 y DA-04.

### DA-18 · Catálogos en ejecución
- **Relación con el SAD:** implementa AD-07, AD-28 y QA-10.
- **Diseño:** Evaluación de Daños tiene `GET/POST/PATCH /v1/catalogos/tipos-activo`, solo para el rol `administrador`. El SAD agrupa estas rutas en una sola fila del Cuadro 15. Un cambio de catálogo emite **EV-25, "Catálogo de tipos de activo actualizado"** (E-16), para que Consulta de Daños conozca los nombres de los tipos nuevos sin redespliegue. El móvil lo recibe en la siguiente descarga.
- **Drivers:** QA-10 (0 componentes modificados y 0 redespliegues) y RF-ED-04 y 05.
- **Costo aceptado:** la validación se hace en ejecución, como en el costo aceptado de AD-07.

### DA-19 · Descarga filtrada e incremental
- **Relación con el SAD:** implementa el Cuadro 15 ("activos de la zona") y corrige H-16.
- **Diseño:** `GET /v1/descarga?desastre={id}&zona={divipola}&desde={versionCatalogo}`.
  - **Desastres:** solo los activos, es decir, los que no están en `CERRADO`.
  - **Activos:** los de los municipios de la zona del desastre, o de la zona que se pida.
  - **`desde`:** devuelve solo los cambios desde esa versión.
  - Se responde con compresión gzip y `ETag`.
- **Drivers:** RF-ED-12, el escenario D, QA-14 y la limitación de conectividad del enunciado.
- **Costo aceptado:** la app debe manejar la descarga incremental y saber cuándo pedir una completa.

### DA-20 · Correlación de punta a punta
- **Relación con el SAD:** implementa el campo `idCorrelacion` del sobre. **No está en los documentos** cómo se propaga.
- **Diseño:** el gateway asigna `traceparent` (W3C) y `X-Id-Correlacion` a cada petición. El servicio lo copia en `idCorrelacion` de los eventos que emite, y cada consumidor lo propaga. Auditoría lo indexa.
- **Drivers:** QA-13 (reconstruir el rastro completo de una acción) y la depuración del flujo distribuido, que es el costo aceptado de AD-02.

---

## 7. Decisiones de tecnología

**Sobre CON-8.** El enunciado (§8) y la restricción CON-8 exigen que la **primera** entrega sea agnóstica a la tecnología, y dicen: "Esto se tendrá en cuenta para la segunda entrega". Por eso **ninguna tecnología está en los documentos**, salvo las del Anexo A del SAD (el prototipo de FP-06). Cada decisión se justifica contra el mecanismo del SAD que materializa. Propongo reunirlas en un "Anexo B: tecnologías de la segunda entrega" (E-17), con el mismo criterio que el Anexo A: "El prototipo puede construirse con otras tecnologías sin modificar ninguna decisión".

| ID | Tecnología | Mecanismo del SAD que materializa | Por qué esta y no otra | Alternativas descartadas |
|---|---|---|---|---|
| DT-01 | **.NET 10 y ASP.NET Core** en todos los servicios, el gateway y los simuladores | AD-01, AD-08 | Ya es la pila de los dos módulos y del Anexo A. Una sola pila reduce el costo de `comun`. CON-2 exige integrarse con sistemas heterogéneos, y eso lo resuelven los adaptadores | Mezclar lenguajes por servicio: no lo pide ningún QA y duplica `comun` |
| DT-02 | **PostgreSQL 18**, una base por servicio y un **rol con mínimo privilegio**, con `GRANT SELECT, INSERT` en los historiales | AD-01, AD-20, AD-30, AD-41, QA-13 | Ya está en uso. La inmutabilidad la garantiza el almacén, no el código (AD-41: "el almacén no admite actualización ni borrado"). Corrige H-14 | Proteger solo en el `DbContext`: lo esquiva cualquier SQL directo |
| DT-03 | **PostGIS 3.5** (en la imagen de CloudNativePG con PostGIS) para Evaluación de Daños, Consulta de Daños, Consulta y Gestión de Desastres | AD-17, DA-07, DA-08, RF-GD-08 | Punto en polígono, agregación y simplificación de geometría (`ST_SimplifyPreserveTopology`) dentro de la base de datos. Es estándar y libre. **Decidido por usted** | Calcular en .NET con NetTopologySuite: es posible, pero traslada a memoria lo que la base hace con índices GiST |
| DT-04 | **RabbitMQ 4.x** con colas quorum, el cliente **RabbitMQ.Client 7** en `comun`, y el Cluster Operator en Kubernetes | AD-02, AD-36, "colas acotadas por consumidor" | Ya está en uso. Ofrece colas por consumidor acotadas (`x-max-length` con `reject-publish`), límite de entregas, DLQ, confirmaciones del productor y quorum con 3 nodos. Encaja exactamente con el Cuadro 5 y la Figura 2 | **Kafka:** no tiene cola acotada por consumidor ni ack por mensaje con reintento y DLQ nativos; la retención del log ayudaría con R-09, pero DA-16 ya lo cubre desde la bandeja. **MassTransit:** sus autores anunciaron en 2025 que la versión 9 tendría licencia comercial; **verifique la licencia vigente** antes de adoptarlo. Además, oculta la topología que el SAD pide que sea explícita |
| DT-05 | **Bandeja y relevo propios** en `comun`, basados en el código actual de Reconstrucción | AD-45, DA-04 | Ya existe y es correcto (`FOR UPDATE SKIP LOCKED` y confirmaciones). Es poco código y está bajo control | El outbox de una biblioteca de terceros: agrega una dependencia por algo que ya está escrito |
| DT-06 | **Redis 8** como caché de geometrías y respuestas de mapa | AD-17, "Motor de caché" de la vista física | **Decidido por usted.** Encaja con TTL e invalidación por clave. Desde 2025 se distribuye también con licencia AGPLv3, que sirve para un proyecto académico. Valkey sería un sustituto compatible | Tabla materializada en PostGIS: es un componente menos, pero el SAD dibuja un motor de caché aparte |
| DT-07 | **YARP 2.x** como gateway, con `Microsoft.AspNetCore.Authentication.JwtBearer` y `System.Threading.RateLimiting` | AD-04, AD-19, R-07 | **Decidido por usted.** Es la misma pila (DT-01). Las particiones del limitador por clase de tráfico implementan AD-19, y validar el catálogo de rutas al arrancar implementa R-07 (DA-12) | Kong y Traefik: la priorización con degradación a caché exigiría plugins propios |
| DT-08 | **Keycloak 26**, OIDC y OAuth 2.1 con PKCE | AD-04, AD-22, QA-11, "gestión de sesiones" | **Decidido por usted.** Es libre y estándar. Ofrece roles, atributos como claims, sesiones, `offline_access` para el móvil y un directorio para validar el responsable (H-33) | Identidad propia: reinventa la seguridad |
| DT-09 | **Microsoft.Extensions.Http.Resilience** (Polly v8) en los adaptadores y en el gateway | AD-05, AD-37, Cuadro 5 | Timeout, reintento exponencial con jitter y cortacircuitos, configurables. Es la biblioteca oficial de .NET | Implementarlo a mano: es propenso a errores |
| DT-10 | **KEDA 2.x**, con un `ScaledObject` por adaptador (*trigger* de RabbitMQ para la salida, `cron` para la entrada) | AD-25, "Nodo de ejecución bajo demanda" | Escala de 0 a N según el largo de la cola, que es exactamente "unidades efímeras que se crean al recibir un evento". En compose los adaptadores quedan siempre encendidos, y se declara | Knative: es más pesado y está orientado a HTTP |
| DT-11 | **MinIO** (la imagen comunitaria actual) para evidencias y documentos | Vista física, "Almacén de archivos", DA-06 | Ya está en uso, con API S3. **Riesgo:** el proyecto original dejó de publicar imágenes (ver el comentario en `docker-compose.yml`). Si el fork falla, la API S3 permite cambiar a Garage o SeaweedFS sin tocar el código | El disco local: lo descarta AD-24 |
| DT-12 | **Angular 22**, **Leaflet 1.9** con teselas de OpenStreetMap, y **angular-auth-oidc-client** | DA-13 | Angular ya está en uso. Leaflet ya lo usa el mapa de Danos. La coropleta se hace con `L.geoJSON` y estilo por índice | MapLibre GL: es mejor con millones de puntos, pero no hace falta con zonas agregadas |
| DT-13 | **AppAuth-Android** con PKCE. Tokens en DataStore cifrado con Android Keystore. WorkManager renueva el token antes de sincronizar | DA-15, AD-31 | Es la biblioteca de referencia de OIDC para Android. Con `offline_access` (un refresh token de larga duración), el evaluador trabaja días sin red y sincroniza al volver. **Riesgo:** si el refresh token vence, hay que volver a iniciar sesión, pero la cola local no se pierde (AD-44) | Pedir usuario y contraseña en la app: viola las buenas prácticas de OAuth |
| DT-14 | **Kubernetes de Docker Desktop**, **CloudNativePG 1.2x** (con *replica clusters*), **RabbitMQ Cluster Operator**, **HAProxy** como enrutador global y **Kustomize** | AD-24, AD-26, AD-27, Figura 3 | **Decidido por usted.** CloudNativePG modela de forma nativa el primario, la réplica local y la réplica en otra región, además de la promoción. Kustomize ya está en uso | Patroni a mano: es más complejo de operar |
| DT-15 | **Sondeos de salud** con `AspNetCore.HealthChecks.NpgSql` y `.RabbitMQ` | AD-24 ("sondeo de salud") | El *readiness* real es lo que permite retirar una réplica fallida en 30 s o menos. **Usted no eligió observabilidad completa**, así que no incluyo Prometheus ni Grafana, pero propongo **logs estructurados en JSON con `idCorrelacion`** como mínimo para depurar. Ver P-07 | — |
| DT-16 | **xUnit**, **Testcontainers for .NET** (PostgreSQL, RabbitMQ y MinIO), **JSON Schema** de los eventos, **k6** para carga y scripts de caos | CON-7 (verificar las medidas), QA-01 a QA-15 | Cada medida numérica necesita su prueba (sección 9, F-11). Testcontainers prueba el relevo, el consumidor y los permisos SQL reales (H-81). k6 genera los 10 000 usuarios virtuales de QA-05 y QA-06 | Probar solo con puertos en memoria: deja sin probar lo que más falla |
| DT-17 | **JSON Schema 2020-12** para el sobre y los eventos, en `comun/esquemas`, validados en una prueba de contrato del productor y del consumidor | AD-09, R-03 | Es legible y sin registro central. La regla aditiva se verifica en CI: un esquema nuevo debe aceptar todos los ejemplos del anterior | Avro y su registro: ver DA-02 |
| DT-18 | **NTP** en los nodos y `IReloj` en el dominio, que ya existe | S-5 | Hay una fuente de tiempo confiable en el servidor. Los relojes de los dispositivos no se usan para detectar conflictos, y eso ya está implementado | — |
| DT-19 | **Migraciones de EF Core** en todos los servicios, ejecutadas por un **Job o contenedor `migrador`** con el rol dueño. Los `GRANT` van dentro de la migración | Despliegue, AD-41 | Corrige H-15 y H-35: el esquema evoluciona y no depende de `initdb`. El servicio corre con un rol sin permisos de DDL | Scripts de `initdb`: solo corren la primera vez |
| DT-20 | **Monorepo** `proyectoparte2`, con **Directory.Packages.props** (versiones centralizadas de NuGet) y una solución por servicio más una solución raíz | Vista de implementación (paquete `comun`) | **Decidido por usted.** `comun` se referencia como proyecto, sin publicar NuGet. La app sigue en `danos` y verifica el contrato contra el OpenAPI que publica el monorepo | Paquetes NuGet internos: piden versionado y un feed |

### 7.1 Estructura del monorepo

```
proyectoparte2/
├── docs/                          (los PDF actuales, sin cambios)
├── comun/
│   ├── Plataforma.Comun.Eventos/      sobre canónico, enumeraciones, esquemas JSON
│   ├── Plataforma.Comun.Mensajeria/   bandeja de salida, relevo, consumidor idempotente, DLQ
│   ├── Plataforma.Comun.Adaptadores/  IContratoAdaptador, marca de avance, resiliencia
│   ├── Plataforma.Comun.Seguridad/    validación JWT, claims, autorización de nivel 2, rechazos a Auditoría
│   └── Plataforma.Comun.Salud/        sondeos vivo/listo
├── servicios/
│   ├── gestion-desastres/         (Dominio, Aplicacion, Infraestructura, Api, Pruebas)
│   ├── consulta/                  (consulta de desastres: proyecciones + caché de geometrías)
│   ├── evaluacion-danos/          (migrado desde danos/api, DA-05)
│   ├── consulta-danos/
│   ├── reconstruccion/            (escritura; el rol "consulta" pasa a ser su propio servicio)
│   ├── consulta-programa/
│   └── auditoria/
├── adaptadores/
│   ├── adaptador-se2/  adaptador-se6/  adaptador-se7/
├── simuladores/
│   ├── sim-se6/  sim-se7/  simulador-qa14/   (este último viene de danos/api/src/Danos.Simulador)
├── gateway/                       (YARP + catálogo de rutas)
├── consola/                       (Angular)
├── despliegue/
│   ├── compose/                   (desarrollo: una región)
│   ├── k8s/base/  k8s/region-a/  k8s/region-b/  k8s/global/
│   ├── keycloak/realm-plataforma.json
│   └── bus/definiciones.json      (topología declarada, DA-03)
├── pruebas/
│   ├── contrato/  carga-k6/  caos/
└── Directory.Packages.props
```

**Por qué la imagen única con dos roles de Reconstrucción pasa a ser dos servicios.** Hoy el servicio de consulta del programa es la misma imagen arrancada con otro rol. Eso funciona, pero:
- Obliga a desplegar el código de escritura en el servicio de lectura.
- Comparte las dependencias.
- No sigue la vista de implementación del SAD, que define "un paquete por microservicio".

**Alternativa:** conservar la imagen única. **Costo:** se pierde la independencia de despliegue de AD-01. Recomiendo separarlos, pero como el código de lectura ya está aislado por rol, es un cambio barato y se puede posponer. Ver P-08.

---

## 8. Matriz de cumplimiento objetivo

Indica qué decisión del SAD queda cubierta por cuál decisión propuesta y en qué fase.

| AD del SAD | Hoy | Objetivo | Decisión y fase |
|---|---|---|---|
| AD-01 Microservicios con almacén propio | Parcial | Sí | DA-05, DA-16, F-0 |
| AD-02 Bus con colas acotadas | Parcial (solo Reconstrucción, sin acotar) | Sí | DA-02, DA-03, F-1 |
| AD-03, AD-09 Adaptador y evento canónico | No | SE-2, SE-6 y SE-7 | DA-02, DA-07, DA-11, F-5 |
| AD-04 API Gateway | No | Sí | DA-12, DA-15, F-6 |
| AD-05, AD-37 Cortacircuitos | No | Sí | DA-11, F-5 |
| AD-06, AD-07 Gestión de Desastres y catálogo | No | Mínimo | DA-10, F-3 |
| AD-08 Cuatro capas | Reconstrucción sí, Danos no | Sí | DA-05, F-2 |
| AD-10 a 13 Consolidación y Notificación | No | Fuera de alcance | §2.3 |
| AD-14 Marca de avance | No | Sí | DA-11, F-5 |
| AD-15, AD-18 CQRS y réplicas de consulta | Parcial | Sí | DA-08, DA-10, DA-16 |
| AD-16 Marca de tiempo del dato | Solo en el mapa de Danos | Sí | DA-08 |
| AD-17 Caché de geometrías | No | Sí | DA-08, DA-10 |
| AD-19 Priorización y degradación | No | Sí | DA-16, F-6 |
| AD-20, AD-30 Historial versionado | Sí (Danos, solo en código) | Sí, con permisos | DT-02 |
| AD-21 Auditoría | No | Sí | DA-17, F-8 |
| AD-22 Autorización en dos niveles | Solo nivel 2, con identidad falsificable | Sí | DA-14, DA-15 |
| AD-23 Cifrado por columna | No aplica (Personas Afectadas) | Fuera de alcance | — |
| AD-24 Réplica activa | Parcial | Sí | DA-16 |
| AD-25 Ejecución híbrida | No | Sí | DT-10 |
| AD-26, AD-27 Dos regiones | No | Simulado | DA-16, F-10 |
| AD-28 Catálogo de tipos de activo | Sembrado | En ejecución | DA-18 |
| AD-29 Registro preliminar | Sí (sin la asociación) | Sí | F-2 |
| AD-31 a 33, 43, 44 Sincronización | Sí | Se conserva | F-2 |
| AD-34 Consulta de Daños | No | Sí | DA-08, F-4 |
| AD-35 a 39 Recursos | No | Fuera de alcance | — |
| AD-40 Proyectos por lote | Sí (sin disparador real) | Sí | DA-01, DA-10 |
| AD-41 Historial de solo inserción | Sí | Sí | — |
| AD-42 Consulta del Programa | Sí (1 réplica) | Sí | DA-16 |
| AD-45 Bandeja de salida | Reconstrucción sí, Danos sin relevo | Todos | DA-04 |

| Riesgo del ATAM | Atención |
|---|---|
| R-01 (arranque del adaptador frente a los 5 s) | No aplica sin SE-8 |
| R-02 (huecos en la auditoría) | `secuenciaProductor` (DA-02 y DA-17) |
| R-03 (contrato del adaptador) | Regla aditiva y versión por evento (DA-02 y DT-17) |
| R-04 (RTO frente al margen) | Límite T-08, con RTO medido (F-10) |
| R-05 (dato erróneo bien formado) | Abierto, fuera del alcance (sin SE-1) |
| R-06 (borrado de datos personales) | No aplica (sin Personas Afectadas) |
| R-07 (ruta sin regla de dominio) | DA-12 |
| R-08 (bandeja sin límite) | DA-09 |
| R-09 (eventos en tránsito al conmutar) | DA-16 (reenvío desde la bandeja) |
| R-10 (estado de la integración) | EV-23 (DA-11) |

---

## 9. Plan por fases

**Orden.** Primero se construye lo que es contrato y base de todo (el sobre, el bus y la plantilla). Después, las piezas en el orden del flujo de negocio (daños, desastres, consulta y reconstrucción). Al final van los transversales y la verificación. Cada fase termina con un **criterio de salida** que se puede comprobar.

Las estimaciones suponen 1 persona con experiencia en .NET y Kubernetes. Son rangos orientativos, no compromisos.

| Fase | Contenido | Depende de | Esfuerzo (días-persona) | Criterio de salida |
|---|---|---|---|---|
| **F-0 · Monorepo y `comun`** | Estructura (§7.1), `Directory.Packages.props`, `comun.Eventos` (sobre, enumeraciones, esquemas), `comun.Mensajeria` (bandeja y relevo extraídos de Reconstrucción, consumidor idempotente), `comun.Salud`, migrador (DT-19) | — | 4–6 | Reconstrucción compila sobre `comun`, sus 43 pruebas pasan y hay pruebas de Testcontainers del relevo |
| **F-1 · Bus** | `definiciones.json` (exchange, colas acotadas, DLQ, alternate exchange), `mandatory`, `x-delivery-limit`, alarma de la DLQ y de `q.sin-ruta`. Reconstrucción emite con el sobre y E-10 | F-0 | 2–3 | Prueba: consumidor caído, 1 000 eventos, consumidor vuelve, 0 perdidos. Prueba: un mensaje envenenado va a la DLQ en 5 intentos sin bloquear la cola |
| **F-2 · Evaluación de Daños en el monorepo** | Migración a 4 capas con puertos (DA-05), MinIO (DA-06), rol con mínimo privilegio (DT-02), relevo de la bandeja, EV-09 extendido, validación y EV-08 (DA-09), resolución y EV-22, asociación, `POST/PATCH /v1/evaluaciones`, catálogo en ejecución con EV-25 (DA-18), descarga filtrada (DA-19), adaptador SE-2 con EV-21 y municipio por PostGIS (DA-07) | F-1 | 10–14 | El simulador de QA-14 da 0 pérdidas, 0 sobrescrituras y 0 duplicados con 2 réplicas. EV-08 llega al bus con el sobre válido |
| **F-3 · Gestión de Desastres mínima y Consulta** | DA-10 completo | F-1, adaptador SE-2 | 10–15 | Un tipo nuevo se crea en ejecución sin redespliegue (QA-10). Una transición inválida para el perfil se rechaza. EV-06 llega a Evaluación de Daños y a Reconstrucción |
| **F-4 · Consulta de Daños** | DA-08: proyecciones, mapa por zona, consolidado, evolución, Redis, `datoAl`, degradación y alarma de R-08 | F-2 | 6–8 | El mapa de un desastre con 5 000 activos responde con p95 < 2 s (prueba k6 local). Si Redis cae, el mapa responde desde PostGIS |
| **F-5 · Reconstrucción integrada y adaptadores** | Consumidores de EV-03, 06, 08 y 24 hacia las copias locales (DA-01), rutas alineadas (E-09), `Nacional` y territorio (DA-14), `datoAl` en el consolidado, procedencia del costo, `adaptador-se6` y `adaptador-se7` con Polly, marca de avance, EV-23, simuladores y KEDA en k8s | F-1, F-2, F-3 | 8–12 | El escenario E de punta a punta. Con SE-7 caído, el proyecto queda creado y, al volver, la cola se entrega. La consola muestra "SE-7 no responde" en 30 s o menos (QA-01) |
| **F-6 · Gateway y Keycloak** | DA-12, DA-15 y DA-16 (limitador por clase), realm, JWT en todos los servicios y fin de los encabezados `X-*` | F-2 a F-5 (las rutas) | 6–8 | Prueba de QA-11: 100 % de rechazos de un organismo no responsable en cada ruta de modificación. Si una ruta no declara `reglaDominio`, el gateway no arranca |
| **F-7 · Consola unificada** | DA-13: login, desastres, mapa (Leaflet con zonas), evaluaciones (validación y bandeja), reconstrucción y auditoría | F-6 | 8–12 | Recorrido completo desde la consola con 4 usuarios de Keycloak, sin selector de perfil |
| **F-8 · Auditoría** | DA-17 | F-1 | 3–4 | QA-15 (b y c): versiones de proyecto igual a entradas de auditoría, cada entrada disponible en 5 s o menos. Un hueco de secuencia provocado se detecta |
| **F-9 · App móvil** (repo `danos`) | AppAuth, tokens, el contrato nuevo (municipio, descarga incremental), la prueba de contrato contra el gateway, `applicationId`, *release* con TLS | F-6 | 4–6 | La prueba instrumentada de contrato pasa contra el gateway. Sincroniza tras 48 h sin red usando el refresh token |
| **F-10 · Dos regiones** | DA-16: CloudNativePG con *replica clusters*, RabbitMQ con 3 nodos, HAProxy, conmutador y reenvío de R-09 | F-5, F-6 | 8–12 | Script de caos que tumba `region-a`: el tráfico se atiende en `region-b` con RTO medido ≤ 15 min y RPO medido ≤ 60 s, y 0 eventos perdidos (QA-03, QA-04) |
| **F-11 · Verificación** | k6 para QA-05, 06 y 07; caos para QA-01, 02 y 04; el simulador de QA-14 a través del gateway; informe de medidas | Todas | 4–6 | Una tabla con la medida obtenida en cada QA en alcance, lista para la sustentación |
| **Total** | | | **73–106** | |

### 9.1 Ruta crítica

**F-0 → F-1 → F-2 → (F-3 en paralelo con F-4) → F-5 → F-6 → F-7.** F-8 puede ir en paralelo desde F-1, y F-9 desde F-6. F-10 va al final, porque necesita todos los servicios.

### 9.2 Si hay que recortar

El orden de recorte que menos daña el cumplimiento:
1. F-10 queda en una sola región con la conmutación de base de datos (la opción 2 de la pregunta que le hice).
2. La Consulta del lado de Gestión de Desastres se reduce a `/v1/consulta/mapa`, sin evolución.
3. El simulador de SE-6 es solo de salida.

**Nunca recortar F-1, F-2 (la validación), F-5 ni F-6.** Son la integración y la seguridad que piden los mínimos.

### 9.3 Riesgos del plan

| ID | Riesgo | Mitigación |
|---|---|---|
| RG-1 | **RAM.** Su PC tiene 15,6 GB. Con las dos regiones (unos 12 servicios × 2, 3+1 nodos de RabbitMQ, unas 8 instancias de PostgreSQL × 2, Keycloak, Redis y MinIO) el consumo estimado ronda los 10–14 GB | `resources.limits` ajustados. `region-b` con 1 réplica y PostgreSQL con `shared_buffers` bajo. Un clúster de PostgreSQL por región con una base por servicio (se mantiene AD-01 lógico, igual que H-63). No correr el emulador durante F-10 |
| RG-2 | Licencias de MinIO o MassTransit | DT-11 y DT-04 |
| RG-3 | Refactor de Danos (DA-05) con regresiones | El simulador de QA-14 y las pruebas de integración actuales se ejecutan antes y después |
| RG-4 | La app se queda con un contrato viejo | OpenAPI versionado (`/v1`) y la prueba de contrato en CI de `danos` |

---

## 10. Esfuerzo del servicio mínimo de Gestión de Desastres

Esta sección responde a su pregunta. El servicio incluye todo lo de la tabla de DA-10, y cada pieza cumple lo que el diseño define para ella.

| Pieza | Días-persona |
|---|---|
| Dominio: desastre con los 12 atributos, máquina de estados como datos, historial versionado (AD-20) y pruebas | 2–3 |
| Catálogo de tipos y perfiles con rutas de administración (QA-10) | 1–1,5 |
| Rutas de registro, severidad, transiciones, zona y organismos, con autorización de nivel 2 (QA-11) | 1,5–2 |
| Zona versionada con PostGIS (municipios y polígono) | 1–1,5 |
| Bandeja, eventos EV-03 a 06, consumo de EV-18 y EV-20 (la regla de cierre) y de EV-21 | 1–1,5 |
| Consulta: proyecciones, `/consulta/desastres`, `/mapa` con caché de geometrías (AD-17) y `/evolucion` | 3–4 |
| Pantallas en la consola (registro, severidad, zona y transiciones) | 1,5–2 |
| **Total** | **11–15,5** |

**Si se omite la Consulta y la consola lee de Gestión de Desastres:** el total baja a 8–11 días. Pero eso contradice AD-15 para FP-02, que es la ruta de mayor carga del sistema, y deja el mínimo "Mapa/visualización de desastres" sin la zona del desastre. No lo recomiendo.

---

## 11. Lo que hoy está bien y se conserva

Para no perderlo en el refactor:

- **Reconstrucción.** Dominio rico con métodos por operación, máquina de estados explícita, lote atómico e idempotente (`lotes_creacion` y el índice parcial `ux_evaluacion_vigente`), historial de solo inserción por `GRANT`, una sola transacción para el cambio, el historial y el evento, y un relevo con `SKIP LOCKED` y confirmaciones.
- **Danos.** La regla de conflicto AD-43 con el dispositivo que produjo cada versión. El reenvío idempotente que guarda el resultado. Una transacción por operación con reintento ante concurrencia. El desfase de reloj (S-5). La verificación del hash. El simulador de QA-14.
- **App.** La cola de operaciones inmutables, WorkManager con la condición de red y la prueba de contrato.

---

## 12. Preguntas abiertas

Estas preguntas no bloquean el inicio, porque F-0 y F-1 no dependen de ellas.

| ID | Pregunta | Propuesta mientras tanto |
|---|---|---|
| P-01 | ¿"Caracterización de un desastre" en el móvil significa solo caracterizar el impacto (lo que hace hoy), o también que el móvil reporte un desastre nuevo, como un "reporte de campo" hacia una futura Consolidación? | Solo el impacto (§2.1). El reporte de campo sería un productor de EV-01, que ya tiene un lugar en el catálogo |
| P-02 | ¿Qué aprueba la autoridad nacional? | Los proyectos con municipios de varios departamentos (DA-14) |
| P-03 | ¿Los activos lineales (vías, redes) se modelan como línea? | Punto obligatorio y línea opcional. El municipio se asigna por intersección mayoritaria (DA-07) |
| P-04 | ¿Cuál es la granularidad de zona del mapa: municipio, o también barrio o vereda? | Municipio obligatorio y barrio opcional cuando haya capa |
| P-05 | ¿Qué fórmula usan el índice de daño por zona y la priorización? | La de DA-08 |
| P-06 | ¿Se exige mTLS entre servicios ("toda comunicación se cifra en tránsito", §7.1 del SAD)? | TLS en el borde y mTLS declarado como límite |
| P-07 | ¿Bastan logs estructurados, o quiere métricas para mostrar las medidas en la sustentación? | Logs estructurados con `idCorrelacion` y medidas tomadas con k6 |
| P-08 | ¿Se separa ya la imagen única de Reconstrucción en dos servicios? | Sí, en F-5 |
| P-09 | ¿Qué fecha de entrega tiene? | Define si se aplica el recorte de §9.2 |
