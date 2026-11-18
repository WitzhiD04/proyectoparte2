# Encargo · Keycloak y la identidad por JWT

> Si `despliegue/keycloak/realm-plataforma.json` y el modo `Jwt` de `comun/Plataforma.Comun.Seguridad` ya existen en la
> rama, este encargo está hecho (ver `docs/encargos/LEEME.md`). La parte 4 es del repositorio de la app (`danos`).

**Alcance:** `despliegue/keycloak`, `comun/Plataforma.Comun.Seguridad` y sus pruebas, compose y k8s. No toque los servicios.

Lea primero, completos:
- `docs/encargos/convenciones.md`, sobre todo **"Contrato de identidad y de entrada"**: el realm, los
  clientes, los claims, los usuarios y los puertos ya están fijados ahí. Sígalo al pie de la letra.
- DA-15 en `docs/decisiones/REVISION_ARQUITECTURA.md`.
- `comun/Plataforma.Comun.Seguridad` (el puerto `IIdentidadActual`, el adaptador de encabezados y `AgregarIdentidad`).

## Parte 1 · Keycloak

1. **`despliegue/keycloak/realm-plataforma.json`**, realm `plataforma`:
   - Los 7 roles de realm.
   - Los atributos de usuario `organizacion`, `ambito` y `territorio`. En Keycloak 26 el perfil de usuario está activo:
     declárelos en el perfil de usuario del realm, o use `unmanagedAttributePolicy: ENABLED`, porque si no, el import los
     descarta.
   - Mapeadores que emiten esos atributos como **claims de primer nivel** en el access token.
   - Los clientes, con PKCE S256 obligatorio en los públicos:

     | Cliente | Configuración |
     |---|---|
     | `consola` | Público. Redirección `http://localhost:8080/*` y web origin `http://localhost:8080` |
     | `movil` | Público. Redirección `co.plataforma.danos:/oauth2redirect`, scope `offline_access`, *Direct Access Grants* activado (solo para las pruebas automáticas; el realm es de desarrollo) |
     | `gateway` | Audiencia: un mapeador de audiencia agrega `gateway` al `aud` de los tokens de `consola` y `movil` |

   - Los 7 usuarios de prueba de `convenciones.md`, con sus atributos y clave `<usuario>_local`.
2. **Compose:** servicio `keycloak` con imagen `quay.io/keycloak/keycloak` en una versión 26 fija, publicada hace más de
   dos semanas.
   - Arranque con `start-dev --import-realm` y el archivo montado en `/opt/keycloak/data/import`.
   - Puerto `8180:8080`.
   - Sondeo de salud con `KC_HEALTH_ENABLED=true`; el endpoint de salud va en el puerto de administración 9000.
3. **El emisor, que es lo más importante.** El token debe llevar **siempre el mismo `iss`**, venga de donde venga la
   petición: `http://localhost:8180/realms/plataforma`.
   - Fíjelo con la configuración de hostname de Keycloak 26 (`KC_HOSTNAME=http://localhost:8180`), y deje que los
     contenedores usen el canal interno (`http://keycloak:8080`) para descubrir las llaves (backchannel dinámico).
   - Verifique con un token pedido desde el host y otro desde un contenedor que el `iss` es idéntico.
4. **K8s:** `despliegue/k8s/base/keycloak.yaml` (Deployment, Service y el realm en un ConfigMap generado desde el mismo
   archivo con `configMapGenerator` en `kustomization.yaml`), más la entrada en `kustomization.yaml`.
   - **Solo escriba, no ejecute nada de kubectl.** Para validar que el YAML genera, puede correr
     `kubectl kustomize --load-restrictor=LoadRestrictionsNone despliegue/k8s/local`, que solo lee archivos.
   - **Nunca** ejecute otro comando de kubectl.

## Parte 2 · Adaptador JWT en `comun/Plataforma.Comun.Seguridad`

- Con `Identidad:Modo = Jwt`, una implementación de `IIdentidadActual` valida el `Authorization: Bearer` de la petición:
  - firma con las llaves del emisor, tomadas de `Identidad:Metadatos` (por ejemplo
    `http://keycloak:8080/realms/plataforma/.well-known/openid-configuration`) y **guardadas en caché**, para que una
    caída corta de Keycloak no invalide los tokens ya emitidos (DA-15);
  - emisor `Identidad:Emisor`, audiencia `Identidad:Audiencia` (`gateway`) y vigencia.
- Construye la misma `Identidad` de hoy: `preferred_username` → usuario; `realm_access.roles` → roles; los claims
  `organizacion`, `ambito` y `territorio`; `X-Dispositivo` se sigue leyendo del encabezado, porque es un dato del
  dispositivo y no identidad.
- **Sin token válido, `Actual` es `null`**, y cada servicio ya responde 401.
- **En modo `Jwt` no se lee ningún `X-Usuario`, `X-Roles`, `X-Organizacion`, `X-Ambito` ni `X-Territorio`.**
- **Que no obligue a cambiar los servicios.** Hoy cada servicio solo llama a `AgregarIdentidad`. El adaptador debe
  validar el token por sí mismo, sin exigir `UseAuthentication()` en cada `Program.cs`.
  - Use `Microsoft.IdentityModel` con `ConfigurationManager<OpenIdConnectConfiguration>`. `JwtBearer` ya está en
    `Directory.Packages.props` y trae esas bibliotecas.
- El modo `Encabezados` sigue funcionando igual y **sigue siendo el valor por omisión**.
- **Pruebas** en `comun/pruebas/Plataforma.Comun.Pruebas`:
  - **Unitarias**, con tokens firmados por una llave de prueba: válido, vencido, otra audiencia, otro emisor, firma
    inválida, y que con un token válido se ignore un `X-Usuario` distinto.
  - **De integración** con `Testcontainers.Keycloak` (ya está en `Directory.Packages.props`), importando
    `realm-plataforma.json`: un token pedido por *password grant* (cliente `movil`, usuario `bomberos.operador`) produce
    la identidad esperada, con roles, organización y audiencia.

## Parte 3 · Compose con el modo configurable y prueba de humo con tokens

- En `despliegue/compose/docker-compose.yml`, a **Gestión de Desastres, Consulta, Consulta de Daños, Evaluación de Daños
  y Auditoría** agrégueles `Identidad__Modo: ${IDENTIDAD_MODO:-Encabezados}`, `Identidad__Emisor`,
  `Identidad__Audiencia` e `Identidad__Metadatos`.
  - **No toque Reconstrucción**: todavía lee su propia identidad y la migra otro agente después.
  - Agregue las variables a `.env.ejemplo`.
- `pruebas/humo/prueba-desastres.sh` gana `MODO_IDENTIDAD=jwt`: pide tokens a Keycloak (*password grant*, cliente
  `movil`, usuarios de prueba) y manda `Authorization: Bearer` en lugar de los `X-*`.
- **Verificación de punta a punta:**
  1. `cd despliegue/compose && IDENTIDAD_MODO=Jwt docker compose -p plataforma up -d --build`.
  2. `MODO_IDENTIDAD=jwt bash pruebas/humo/prueba-desastres.sh` debe terminar en "Todo bien".
  3. Con `IDENTIDAD_MODO=Jwt`, una petición con solo `X-Usuario` debe recibir 401.
  4. Vuelva a levantar sin la variable: el modo por omisión debe seguir pasando.

  Puede usar el compose: nadie más lo está usando. **Nunca `down -v`.**

## Parte 4 · El emisor en la app (repo `danos`, hoy en la rama `main`)

- Cambie el emisor por omisión a `http://localhost:8180/realms/plataforma`. El emulador llega a él con
  `adb reverse tcp:8180 tcp:8180`, así el `iss` coincide con el que validan los servicios.
- Documente ese paso en `danos/README.md` y en `ContratoServidorTest`.
- Si hay un emulador disponible, corra `ContratoServidorTest` en modo OIDC contra Evaluación de Daños en compose con
  `IDENTIDAD_MODO=Jwt`; si no, déjelo documentado.

## Reporte final (péguelo en su respuesta)

1. Qué hizo por parte, con archivos.
2. Las decisiones que tomó, por ejemplo la versión de Keycloak, cómo fijó el emisor y cómo cachea las llaves.
3. Resultado de las pruebas: unitarias, Testcontainers y la prueba de humo con `jwt`, con números.
4. Lo que no pudo verificar.
5. Contradicciones que haya encontrado.
