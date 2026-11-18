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

La suite completa está en verde (`bash herramientas/dotnet.sh test Plataforma.sln`) y en compose pasan las cuatro pruebas
de humo de `pruebas/humo/` (`prueba-humo.sh`, `prueba-desastres.sh`, `prueba-qa11.py` y `prueba-resolucion-gateway.sh`).
Las medidas por escenario de calidad están en `docs/decisiones/REVISION_ARQUITECTURA.md` (sección "Medidas").

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
| API Gateway (YARP), catálogo de rutas, priorización, correlación y QA-11 | `gateway/`, `pruebas/humo/prueba-qa11.py` | AD-04, DA-12 (56 rutas tras retirar `/v1/mapa-calor`, R-07 al arrancar), DA-15 (JWT nivel 1 y reenvío sin `X-*`), DA-20 (`traceparent` y `X-Id-Correlacion`), AD-19 y DA-16 (priorización por clase con degradación de `/v1/consulta*`), QA-11 medido al 100 % (198/198 rechazados) y renovación de conexiones (`PooledConnectionLifetime`) |
| Consola con inicio de sesión (B1) | `consola/`, `despliegue/keycloak/temas/plataforma` | DA-13 y DT-12: OIDC con PKCE contra Keycloak (cliente `consola`), interceptor con Bearer y renovación, menú por los roles del token sin selector de perfil y módulo `reconstruccion` migrado. La pantalla de Keycloak usa un tema con la identidad de la consola |
| Evolución por zona en Consulta de Daños (A4) | `servicios/consulta-danos` (`ConsultarEvolucionZona`), ruta `cd-evolucion-zona` del gateway, E-40 | RF-ED-15 por zona: serie por municipio DIVIPOLA con su autor e instante; `PruebasEvolucionZona.cs` (13 pruebas) |
| Mapa de la consola (B2) | `consola/src/app/modulos/mapa`, capturas en `consola/docs/mapa-*.png` | RF-ED-11, RF-GD-17 y RF-ED-15: mapa Leaflet de tres capas (municipios, zonas de desastre y activos por nivel de daño), barra de tiempo que consulta la zona vigente en un instante y paneles de evolución por activo y por zona; `mapa.spec.ts` (24 pruebas) |
| Tablero de daños en Razor Pages (C1) | `servicios/consulta-danos/src/ConsultaDanos.Api`, `despliegue/keycloak` | E-41: excepción a DA-13 como segundo adaptador de entrada sobre `ConsultarConsolidado` y `ConsultarIndicadores`. OIDC con PKCE y cookie, inicio de sesión único con la consola, acceso por rol y enlace en el menú; verificado en compose por el gateway |
| Módulo de evaluaciones de la consola (B3) | `consola/src/app/modulos/evaluaciones`, `consola/src/app/modulos/mapa` | RF-ED-08 y RF-ED-13: validación con motivo, bandeja de conflictos con las dos versiones lado a lado y las cuatro decisiones, catálogo de tipos de activo y evolución por zona desde el mapa (E-40); verificado en compose por el gateway |
| App móvil con OIDC y registro del desastre en campo | repositorio `danos`, rama `main` (APK en `danos/entrega/DanosCampo.apk`) | H-70 a H-72: OIDC con AppAuth, descarga incremental y migración Room. Registro y caracterización del desastre en campo (RF-GD-02, 03, 05 y 08) con cola local y reenvío idempotente (`POST /v1/desastres` con `id` del cliente). La prueba instrumentada `ContratoServidorTest` pasa contra el gateway y Keycloak desde el emulador (7 de 7, `adb reverse tcp:8180 tcp:8180`) |

## Alcance de esta entrega y reparto (fijado el 2026-11-17)

Se implementan **Móvil, Evaluación de Daños, Mapa y visualización de desastres, y Reconstrucción**. Los encargos de
gateway, consola, carga y caos, y cierre llevan al inicio una nota que los recorta a ese alcance.

| Encargo | En esta entrega | Persona |
|---|---|---|
| `reconstruccion-identidad.md` | Completo | A |
| `gateway.md` | Recortado: sin rechazos a Auditoría | A |
| `adaptador-se6.md` | Completo: es parte del diseño de Reconstrucción (presupuesto y costos al sistema financiero) | A |
| Evolución por zona en Consulta de Daños (A4) | **Hecho** | A |
| Tablero de daños en Razor Pages (C1) | **Hecho** (E-41) | A |
| `consola.md` | Recortado: login, `mapa`, `evaluaciones` y `reconstruccion` | B |
| App (`danos`) contra Keycloak y el gateway | **Hecho**: prueba instrumentada, registro de desastres en campo y APK 2.1.0 | B |
| `../../pruebas/ENCARGO-carga-y-caos.md` | **No ejecutado**: se declara como límite (L-03). QA-06 y QA-07 tienen una medida de referencia con `pruebas/medidas/latencias.py` | A y B |
| `cierre-y-medidas.md` | **Hecho en compose** (sin dos regiones; Kubernetes sin verificar de punta a punta) | A y B |
| `notificacion-se8.md` | **Fuera**: es de Gestión de Desastres | — |

A es la integradora: fusiona a `main` y corre la suite y el compose. Orden real de fusión (`git log --graph`):
Reconstrucción con identidad → jurisdicción en lecturas → gateway → consola con login → evolución por zona (A4) → SE-6 →
mapa (B2) → tablero Razor (C1) → evaluaciones (B3) → cierre. La app vive en el repositorio `danos` y no se fusiona aquí.
Carga y caos no se ejecutaron (ver límites en `docs/decisiones/REVISION_ARQUITECTURA.md`).

## Lo que falta, en orden

| # | Encargo | Depende de | Notas |
|---|---|---|---|
| 1 | `keycloak-jwt.md` | — | **Hecho** |
| 2 | `gateway.md` | 1 | **Hecho** (recortado: sin rechazos a Auditoría) |
| 3 | `reconstruccion-identidad.md` | 1 | **Hecho** |
| 4 | `consola.md` | 1 (y 2 para el recorrido final) | **Hecho** (recortado: login, `mapa`, `evaluaciones` y `reconstruccion`) |
| 5 | `notificacion-se8.md` | — | **Fuera de esta entrega**: es de Gestión de Desastres; se declara como límite del prototipo |
| 6 | `adaptador-se6.md` | — | **Hecho** |
| 7 | `../../pruebas/ENCARGO-carga-y-caos.md` | Compose arriba | **No ejecutado**: se declara como límite de la entrega |
| 8 | `cierre-y-medidas.md` | Todos | **Hecho en compose**: limpieza del mapa de calor, tabla de medidas, límites y recorrido de uso. Kubernetes en Minikube queda sin verificar de punta a punta |

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
| Llaves de la cookie del tablero sin cifrar en Redis | C1 | Declarado: si Redis las expulsa, la sesión se rehace con el inicio de sesión único |
| La consola no lista las evaluaciones rechazadas | B3 | Declarado: el modelo de lectura las excluye; se reabren por la API |
