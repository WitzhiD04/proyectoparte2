# Encargo · Gateway (YARP): catálogo de rutas, priorización, QA-11 y rechazos a Auditoría

Lea primero `docs/encargos/convenciones.md` completo, en especial **"Contrato de identidad y de entrada"**. Manda sobre
este encargo.

**Prerrequisitos (ya en el repositorio):**
- Keycloak con el realm `plataforma` (`despliegue/keycloak/`, en compose en el puerto 8180).
- El adaptador JWT de `IIdentidadActual` en `comun/Plataforma.Comun.Seguridad`.
- El modo de identidad configurable en compose (`IDENTIDAD_MODO=Jwt`).

Verifíquelos antes de empezar. Si faltan, haga primero el encargo de Keycloak y JWT (ver `docs/encargos/LEEME.md`).

## Decisiones que rigen

Todas están en `docs/decisiones/REVISION_ARQUITECTURA.md`. Lea completos AD-04, AD-19 y AD-22 y el §7.1 del SAD
(`docs/SADArqui.pdf`).

| Decisión | Qué fija |
|---|---|
| DA-12 | Catálogo de rutas con regla de dominio obligatoria (R-07) |
| DA-15 | Identidad en dos niveles; el JWT se revalida en cada servicio |
| DA-16 | De esta decisión, solo la priorización y la degradación (AD-19) |
| DA-20 | Correlación |
| DT-07 | YARP 2.x con `JwtBearer` y `System.Threading.RateLimiting` |

**Escenario:** QA-11, rechazar el 100 % de las modificaciones de un organismo no responsable, en cada ruta.

## Alcance

1. **`gateway/`** (YARP 2.x, en `Directory.Packages.props`). Proyecto con pruebas, Dockerfile y README.
   - **Catálogo de rutas** de todos los servicios del repositorio, alineado con el Cuadro 15 del SAD y con E-09.
     - Cada ruta lleva los metadatos `servicio`, `roles` (nivel 1), `claseTrafico` (`consola`, `movil` o `portal`) y
       `reglaDominio` (`ninguna` o el nombre de la regla de nivel 2 del servicio).
     - **El gateway no arranca** si una ruta POST, PUT, PATCH o DELETE no declara `reglaDominio` (R-07). Agregue una
       prueba de eso.
   - **Valida el JWT** con el mismo emisor y audiencia que los servicios.
     - Aplica el rol de la ruta: sin token responde 401, con un rol que no corresponde 403.
     - **Reenvía el mismo JWT**: los servicios lo vuelven a validar.
   - **Correlación (DA-20).** Asigna `traceparent` y `X-Id-Correlacion` a cada petición.
     - Verifique que los servicios lo copien en `idCorrelacion` de los eventos que emiten (`EscritorBandeja` en
       `comun/Plataforma.Comun.Mensajeria`).
     - Si no lo hacen, cámbielo con cuidado y con prueba.
   - **Priorización (AD-19).** El limitador tiene particiones por `claseTrafico`, con concurrencia reservada para la consola.
     - Cuando una consulta (GET de `/v1/consulta*`) supera su cupo, responde con **la última respuesta guardada, marcada
       como degradada** (`X-Degradado: true`, y `degradado: true` si el cuerpo es JSON), no con 503 (CON-6).
     - Agregue una prueba que sature el portal y muestre que la consola sigue dentro de su objetivo (QA-05 e).
   - **Sirve la consola.** La imagen `web` queda detrás del gateway, y nginx deja de enrutar la API.
   - Se publica en el puerto **8080** del host.
2. **QA-11 medido.** Una prueba que recorra cada ruta de modificación del catálogo que tenga regla de dominio. En cada
   una, hace la petición con el token de un organismo no responsable (usuario `bomberos.operador` frente a un desastre
   cuyo responsable es `ungrd`, y lo equivalente en cada servicio) y verifica el rechazo en el 100 % de los casos.
   Imprima la tabla ruta → código.
3. **Rechazos a Auditoría (DA-15, §7.1).** Tanto el rechazo de nivel 1 (en el gateway) como el de nivel 2 (en el
   servicio) deben quedar en el registro de auditoría.
   - El catálogo no tiene un evento para eso: agréguelo de forma **aditiva** en `comun/Plataforma.Comun.Eventos`, con
     su registro, su esquema, un ejemplo en `EjemplosContrato` y la cuenta en la prueba del catálogo.
   - Registre la enmienda siguiente en `docs/decisiones/ENMIENDAS_DOCUMENTOS.md`.
   - **El gateway no tiene base de datos:** decida y justifique cómo publica sin perder el evento si el bus no responde
     (por ejemplo, una bandeja local pequeña).
   - Auditoría acepta tipos nuevos (E-34); agréguele la interpretación (`InterpretacionEventos.cs`).
4. **Despliegue.**
   - Compose: el gateway con 2 réplicas, y todos los servicios con `IDENTIDAD_MODO=Jwt` por omisión.
   - K8s: `despliegue/k8s/base/gateway.yaml`, con PDB y 2 réplicas, más su entrada en `kustomization.yaml`.
   - Las pruebas de humo (`pruebas/humo/*.sh`) pasan por el gateway con tokens.

**Reconstrucción** todavía lee su propia identidad (`X-Rol`). Si el encargo de Reconstrucción
(`docs/encargos/reconstruccion-identidad.md`) aún no está hecho, deje sus rutas en el catálogo con
`reglaDominio: reconstruccion-propia` y anótelo.

## Criterio de salida

- `bash herramientas/dotnet.sh test Plataforma.sln` en verde.
- Compose completo detrás del gateway, con las dos pruebas de humo pasando con tokens.
- QA-11 al 100 %.
- El gateway rechaza arrancar con una ruta sin regla.
- La degradación por clase de tráfico está probada.
- Reporte final con el formato de `convenciones.md`.
