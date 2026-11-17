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
| Adaptador de SE-7, simulador y KEDA | `adaptadores/adaptador-se7`, `simuladores/sim-se7` | QA-01 con SE-7 |
| Contrato de Notificación (EV-07, 28 y 29) | `comun/` | E-37 |
| Keycloak (realm `plataforma`) y adaptador JWT de `IIdentidadActual` | `despliegue/keycloak`, `comun/Plataforma.Comun.Seguridad` | DA-15: con `IDENTIDAD_MODO=Jwt` los servicios validan el token y rechazan los `X-*` (401); la prueba de humo de desastres pasa con tokens. Reconstrucción aún lee su propia identidad |
| App móvil con OIDC | repositorio `danos`, rama `gemini-app-oidc` | H-70 a H-72. Falta correr su prueba instrumentada contra Keycloak (emulador con `adb reverse tcp:8180 tcp:8180`) |

## Lo que falta, en orden

| # | Encargo | Depende de | Notas |
|---|---|---|---|
| 1 | `keycloak-jwt.md` | — | **Hecho** |
| 2 | `gateway.md` | 1 | Lo más grande que queda. Habilita QA-11 medido, AD-19 y los rechazos a Auditoría |
| 3 | `reconstruccion-identidad.md` | 1 | Puede ir en paralelo con el 2 |
| 4 | `consola.md` | 1 (y 2 para el recorrido final) | Puede empezar en cuanto exista Keycloak |
| 5 | `notificacion-se8.md` | — | Independiente; RF-GD-06 lo exige |
| 6 | `adaptador-se6.md` | — | Independiente |
| 7 | `../../pruebas/ENCARGO-carga-y-caos.md` | Compose arriba | Scripts k6 y de caos |
| 8 | `cierre-y-medidas.md` | Todos | Tabla de medidas, k8s en Minikube y documentación |

**En paralelo:** 2, 3, 5, 6 y 7 se pueden hacer a la vez, cada uno en su rama. Después de cada uno, el orquestador
fusiona y corre la suite completa.

**Archivos compartidos:** compose, `kustomization.yaml`, `00-base.yaml`, `.env.ejemplo`, `aplicar.sh` y `Plataforma.sln`
generan conflictos al fusionar. Se resuelven conservando los dos lados. Para la solución, conviene volver a registrar
los proyectos con `dotnet sln add`.
