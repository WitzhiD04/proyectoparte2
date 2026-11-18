# Encargos: estado del proyecto y lo que falta

Cada encargo es autocontenido: se le da a un agente con esta instrucción:

> Lee y cumple completo `docs/encargos/<encargo>.md`.

Todos parten de `docs/encargos/convenciones.md`, que es obligatorio leer primero. Las fuentes de verdad son:

| Fuente | Dónde |
|---|---|
| PDF del SRS, SAD, ATAM y Diseño de Reconstrucción | `docs/` (se leen con `pdftotext -layout`) |
| El enunciado | `docs/ENUNCIADO.pdf` y `docs/decisiones/A_IMPLEMENTAR.md` |
| Decisiones tomadas: hallazgos H, decisiones DA y DT, plan | `docs/decisiones/REVISION_ARQUITECTURA.md` |
| Correcciones aditivas a los documentos (E-01 en adelante) | `docs/decisiones/ENMIENDAS_DOCUMENTOS.md` |

## Hecho y verificado

La suite completa está en verde (`bash herramientas/dotnet.sh test Plataforma.sln`) y en compose pasan las dos pruebas
de humo.

| Pieza | Dónde | Qué demuestra |
|---|---|---|
| Contrato de eventos, bus de 3 nodos, bandeja de salida, consumidor idempotente, migrador | `comun/`, `despliegue/bus` | AD-02, AD-45, QA-04 (0 eventos perdidos) |
| Gestión de Desastres mínima | `servicios/gestion-desastres` | RN-1 a RN-4, QA-10, QA-11, cierre bloqueado con proyectos abiertos |
| Consulta (lectura de desastres) | `servicios/consulta` | AD-15 a AD-17, `datoAl`, degradación |
| Evaluación de Daños y adaptador de SE-2 | `servicios/evaluacion-danos`, `adaptadores/adaptador-se2` | QA-14 con 2 réplicas, municipio por PostGIS |
| Consulta de Daños | `servicios/consulta-danos` | Mapa de tres capas, priorización, R-08 |
| Reconstrucción por eventos | `servicios/reconstruccion` | DA-01 y el escenario E de punta a punta |
| Auditoría | `servicios/auditoria` | QA-13, QA-15, R-02 |
| Adaptador de SE-6, simulador y KEDA | `adaptadores/adaptador-se6`, `simuladores/sim-se6` | DA-11 y E-10: salida por lotes de EV-18, EV-19 y EV-20 financiero hacia SE-6, cortacircuitos con EV-23 y reintento sin pérdida; QA-01 con SE-6 verificado en pruebas de integración y en compose |
| Adaptador de SE-7, simulador y KEDA | `adaptadores/adaptador-se7`, `simuladores/sim-se7` | QA-01 con SE-7 |
| Contrato de Notificación (EV-07, 28 y 29) | `comun/` | E-37 |
| Keycloak (realm `plataforma`) y adaptador JWT de `IIdentidadActual` | `despliegue/keycloak`, `comun/Plataforma.Comun.Seguridad` | DA-15: con `IDENTIDAD_MODO=Jwt` los servicios validan el token y rechazan los `X-*` (401); la prueba de humo de desastres pasa con tokens |
| Reconstrucción con identidad común y jurisdicción | `servicios/reconstruccion` | DA-15: Reconstrucción y la Consulta del Programa usan `IIdentidadActual`. DA-14: alcance `Nacional`, aprobación por territorio DIVIPOLA y consolidado por jurisdicción; la prueba de humo del escenario E pasa con encabezados y con tokens |
| Jurisdicción en lecturas, creación y acciones de la autoridad | `servicios/reconstruccion`, E-39 | Cuadro 3 del SRS: la autoridad solo ve candidatas, proyectos, detalle, historial y consolidado de su jurisdicción; un lote con evaluaciones ajenas da 403 y no crea ninguno; detalle, historial, asignar, aprobar y cancelar dan 404 fuera de ella (aprobar conserva el 403 para quien lo ve pero no lo aprueba). Prueba de humo con Calarcá en los dos modos |
| API Gateway (YARP), catálogo de rutas, priorización, correlación y QA-11 | `gateway/`, `pruebas/humo/prueba-qa11.py` | AD-04, DA-12 (57 rutas, R-07 al arrancar), DA-15 (JWT nivel 1 y reenvío sin `X-*`), DA-20 (`traceparent` y `X-Id-Correlacion`), AD-19 y DA-16 (priorización por clase con degradación de `/v1/consulta*`), QA-11 medido al 100 % (198/198 rechazados) y renovación de conexiones (`PooledConnectionLifetime`) |
| Consola con inicio de sesión (B1) | `consola/`, `despliegue/keycloak/temas/plataforma` | DA-13 y DT-12: OIDC con PKCE contra Keycloak (cliente `consola`), interceptor con Bearer y renovación, menú por los roles del token sin selector de perfil, módulo `reconstruccion` migrado y `mapa` y `evaluaciones` como módulos diferidos vacíos (B2 y B3). La pantalla de Keycloak usa un tema con la identidad de la consola |
| App móvil con OIDC | repositorio `danos`, rama `gemini-app-oidc` | H-70 a H-72. Falta correr su prueba instrumentada contra Keycloak (emulador con `adb reverse tcp:8180 tcp:8180`) |

## Alcance de esta entrega y reparto (fijado el 2026-11-17)

Se implementan **Móvil, Evaluación de Daños, Mapa y visualización de desastres, y Reconstrucción**. Los encargos de
gateway, consola, carga y caos, y cierre llevan al inicio una nota que los recorta a ese alcance.

| Encargo | En esta entrega | Persona |
|---|---|---|
| `reconstruccion-identidad.md` | Completo | A |
| `gateway.md` | Recortado: sin rechazos a Auditoría | A |
| `adaptador-se6.md` | Completo: es parte del diseño de Reconstrucción (presupuesto y costos al sistema financiero) | A |
| Evolución por zona en Consulta de Daños | Pendiente menor que sí se hace | A |
| `consola.md` | Recortado: login, `mapa`, `evaluaciones` y `reconstruccion` | B |
| App (`danos`) contra Keycloak y el gateway | Prueba instrumentada y APK nuevo | B |
| `../../pruebas/ENCARGO-carga-y-caos.md` | Recortado: sin QA-07. Caos lo hace A, carga con k6 lo hace B | A y B |
| `cierre-y-medidas.md` | Recortado: sin dos regiones | A y B |
| `notificacion-se8.md` | **Fuera**: es de Gestión de Desastres | — |

A es la integradora: fusiona a `main` y corre la suite y el compose. Orden de fusión: Reconstrucción con identidad →
gateway → consola con login → mapa → SE-6 → evolución por zona → evaluaciones → app → caos y carga → cierre.

## Lo que falta, en orden

| # | Encargo | Depende de | Notas |
|---|---|---|---|
| 1 | `keycloak-jwt.md` | — | **Hecho** |
| 2 | `gateway.md` | 1 | **Hecho** (recortado: sin rechazos a Auditoría) |
| 3 | `reconstruccion-identidad.md` | 1 | **Hecho** |
| 4 | `consola.md` | 1 (y 2 para el recorrido final) | Puede empezar en cuanto exista Keycloak |
| 5 | `notificacion-se8.md` | — | **Fuera de esta entrega**: es de Gestión de Desastres; se declara como límite del prototipo |
| 6 | `adaptador-se6.md` | — | **Hecho** |
| 7 | `../../pruebas/ENCARGO-carga-y-caos.md` | Compose arriba | Scripts k6 y de caos |
| 8 | `cierre-y-medidas.md` | Todos | Tabla de medidas, k8s en Minikube y documentación |

**En paralelo:** 2, 6 y 7 se pueden hacer a la vez, cada uno en su rama. Después de cada uno, el orquestador
fusiona y corre la suite completa.

**Archivos compartidos:** compose, `kustomization.yaml`, `00-base.yaml`, `.env.ejemplo`, `aplicar.sh` y `Plataforma.sln`
generan conflictos al fusionar. Se resuelven conservando los dos lados. Para la solución, conviene volver a registrar
los proyectos con `dotnet sln add`.

## Pendientes menores documentados (no tienen encargo propio)

Salieron de los reportes de cada pieza. Recomendación de qué hacer con cada uno:

| Pendiente | Origen | Recomendación |
|---|---|---|
| Registro de auditoría cuando se dispara la alarma de la bandeja (R-08) | DA-09 lo pide; Consulta de Daños no publica eventos | **Hacerlo** junto con el evento de rechazos del gateway (mismo patrón: evento nuevo aditivo + interpretación en Auditoría). Hay que dar permiso de publicar al usuario `consulta-danos` en `despliegue/bus/generar_definiciones.py` |
| Auditoría filtra por la jurisdicción de la autoridad | DA-14; hoy toda autoridad ve todo el rastro | Opcional. Si no se hace, se declara: QA-13 no lo exige |
| `ETag` en la descarga | DA-19 | Opcional; la descarga ya es incremental y comprimida. Declararlo |
| Un cliente incremental no se entera de que un activo se fusionó | Paso 2 | Opcional: que la descarga incremental incluya los activos marcados `fusionadoEn`. Declararlo si no se hace |
| EV-23 no publica el estado SEMIABIERTO | Adaptador de SE-7 (decisión D2) | Aceptado: evita un evento cada 15 s. Ya documentado |
| En k8s la entrada de SE-7 corre por `cron` (latencia de EV-24 de ~2 min) | Adaptador de SE-7 (D6) | Aceptado; el README explica cómo dejarla siempre encendida |
| SE-2 usa su propia base `adaptador_se2` y no `adaptadores` + esquema como dice DA-11 | Paso 2 | Opcional unificar; no cambia ninguna medida |
| mTLS entre servicios | §7.1 del SAD, P-06 | Declarado como límite del prototipo (TLS en el borde) |
| Dos regiones (QA-03) | DA-16 | Opcional, al final (ver `cierre-y-medidas.md`) |
| `p_historial_activo` sin índice por municipio | A4 | Declarado: la evolución por zona recorre la tabla; con los datos de la entrega no se nota. La corrección es un índice por `(codigo_municipio, instante)` en una migración |
| El validador JWT no recarga las llaves si Keycloak las cambia | B1 | Declarado: reiniciar los servicios tras recrear Keycloak; la corrección es volver a pedir la configuración ante una llave desconocida |
