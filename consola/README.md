# Consola de coordinación (Angular)

Una sola consola para los actores de la plataforma (DA-13). Se entra con el usuario de Keycloak y cada persona ve las
secciones que permiten los roles de su token. Se sirve con nginx **detrás del API Gateway**: consola y API entran por
http://localhost:8080.

## Inicio de sesión

- OIDC con código y **PKCE S256** contra Keycloak: realm `plataforma`, cliente `consola`, emisor
  `http://localhost:8180/realms/plataforma` (`angular-auth-oidc-client`, DT-12).
- El interceptor (`nucleo/autenticacion/interceptor.ts`) agrega `Authorization: Bearer` a las peticiones de `/v1`. Renueva
  el token si está por vencer; ante un 401 renueva y repite la petición una vez. Si no puede, vuelve a la pantalla de
  ingreso y, al entrar de nuevo, regresa a donde estaba el usuario.
- Ante un 403 o un 404, la pantalla muestra el mensaje del servicio.
- Las guardas y el menú son comodidad de la interfaz: quien autoriza es el gateway y cada servicio.
- La pantalla donde se escribe la clave es de Keycloak y usa el tema `despliegue/keycloak/temas/plataforma`, con la misma
  paleta y tipografías de esta consola. Si cambia `src/styles.css`, actualice también su `plataforma.css`.

Los usuarios de prueba están en `docs/encargos/convenciones.md` (clave: usuario + `_local`).

## Módulos (carga diferida)

| Ruta | Módulo | Roles | Estado |
|---|---|---|---|
| `/reconstruccion` | Candidatas, proyectos y consolidado | `autoridad`, `responsable-proyecto`, `auditor` | Listo |
| `/mapa` | Mapa de daños y desastres | todos menos `evaluador` | Vacío (tarea B2) |
| `/evaluaciones` | Validación, conflictos y catálogo | `autoridad`, `organismo`, `operador`, `administrador` | Vacío (tarea B3) |

El menú se declara en `nucleo/menu.ts`. Un módulo nuevo es una entrada allí y una ruta en `app.routes.ts`.

## Compilar y probar

Requiere Node 22.22.3 o superior, o 24.15 o superior.

```bash
npm ci
npx ng build
npx ng test --watch=false
```

El cliente `consola` de Keycloak admite como dirección de regreso `http://localhost:8080` (compose),
`http://localhost:30080` (k8s) y `http://localhost:4200` (`npm start`, que envía `/v1` al gateway).

Para ver los cambios en la plataforma, reconstruya solo la consola:

```bash
docker compose -f despliegue/compose/docker-compose.yml -p plataforma up -d --build --no-deps web
```
