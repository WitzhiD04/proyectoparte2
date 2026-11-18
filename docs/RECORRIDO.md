# Recorrido de uso y lista de capturas

Recorrido de punta a punta de los cuatro mínimos de la entrega: a) registro y caracterización de un desastre desde el
móvil, d) evaluación de daños en campo y su revisión, c) mapa y visualización, y b) reconstrucción física. Cada paso
indica quién lo hace, dónde y qué captura tomar. Las capturas se guardan en `docs/capturas/` con el nombre indicado.

## Preparación

1. Plataforma arriba: `docker compose -f despliegue/compose/docker-compose.yml up -d --build`.
   - Si el puerto 8080 del computador está ocupado, use otro: `PUERTO_WEB=18080 PUERTO_WEB_REPLICA=18081 docker compose ...`.
     La **consola** solo inicia sesión en `http://localhost:8080` (redirecciones del cliente `consola` en Keycloak); la
     app móvil funciona en cualquier puerto.
2. Emulador Android con la app: `adb install -r danos/entrega/DanosCampo.apk` y `adb reverse tcp:8180 tcp:8180`.
   - En la pantalla **Ingreso** de la app: URL `http://10.0.2.2:8080` (o el puerto elegido) y emisor
     `http://localhost:8180/realms/plataforma`.
   - La primera vez, Chrome del emulador muestra su pantalla de bienvenida antes de la de Keycloak: acéptela a mano.
3. Usuarios de prueba (clave = usuario + `_local`): `bomberos.operador` (organismo), `evaluador.armenia` (evaluador),
   `operador.danos` (operador), `alcaldia.armenia` (autoridad de Armenia), `ing.perez` (responsable de proyecto),
   `auditor.1` (auditor).

## Paso a paso

| # | Mínimo | Quién y dónde | Acción | Captura |
|---|---|---|---|---|
| 1 | a | App, pantalla Ingreso | Escribir la URL del gateway y tocar **Probar conexión**: aparece "Conexión correcta con el servidor" | `01-app-ingreso-conexion.png` |
| 2 | a | App → Keycloak | **Iniciar sesión** como `bomberos.operador` (pantalla de Keycloak con el tema de la plataforma) | `02-app-keycloak.png` |
| 3 | a | App, modo avión | Activar el modo avión del emulador. Ícono de alerta → **Registrar desastre**: tipo Inundación, nombre, descripción, nivel 2, inició hace 1 h, responsable, ubicación capturada y radio de 500 m | `03-app-registrar-desastre.png` |
| 4 | a | App, lista de desastres | Guardar: el registro queda **Pendiente de envío** (sin red) | `04-app-desastre-pendiente.png` |
| 5 | a | App | Quitar el modo avión y tocar **Sincronizar**: el registro pasa a **Registrado en la plataforma** | `05-app-desastre-registrado.png` |
| 6 | a | App | Cerrar sesión e iniciar como `evaluador.armenia`. Registrar un desastre: queda **Rechazado** con el motivo de rol (RF-GD-02) | `06-app-desastre-rechazado-rol.png` |
| 7 | d | App, Descarga | Como `evaluador.armenia`: **Descarga** del desastre del paso 5 y del municipio 63001 | `07-app-descarga.png` |
| 8 | d | App, modo avión | Nueva evaluación: activo nuevo (vivienda), nivel Grave, descripción y una foto. Repetir con un segundo activo | `08-app-evaluacion.png` |
| 9 | d | App, lista | Las dos evaluaciones en **Pendiente**, con el contador de la cola | `09-app-cola-pendiente.png` |
| 10 | d | App | Quitar el modo avión y sincronizar: **Confirmada** y estado **Registrada** | `10-app-sincronizada.png` |
| 11 | d | Consola (`operador.danos`), Evaluaciones → Validación | Validar una evaluación con motivo | `11-consola-validacion.png` |
| 12 | d | Consola, Evaluaciones → Conflictos | Abrir un conflicto (por ejemplo, uno de los 5 del simulador QA-14) y ver las dos versiones lado a lado | `12-consola-conflicto.png` |
| 13 | d | Consola, Evaluaciones → Catálogo | Ver los 11 tipos de activo con sus criterios por nivel | `13-consola-catalogo.png` |
| 14 | c | Consola (`alcaldia.armenia`), Mapa | Elegir el desastre del paso 5 con las tres capas encendidas (municipios, activos y zona) | `14-consola-mapa-tres-capas.png` |
| 15 | c | Consola, Mapa | Mover la barra de tiempo a un instante anterior a la reclasificación o a la nueva zona | `15-consola-mapa-barra-tiempo.png` |
| 16 | c | Consola, Mapa | Clic en un activo: evolución del activo; clic en el municipio: evolución por zona | `16-consola-evolucion-activo-zona.png` |
| 17 | c | Consola, Tablero de daños | Priorización por municipio y nivel (`/tablero-danos`) | `17-consola-tablero.png` |
| 18 | b | Consola (`alcaldia.armenia`), Reconstrucción → Candidatas | Las evaluaciones Grave/Destrucción total validadas aparecen como candidatas; crear proyectos por lote | `18-consola-candidatas.png` |
| 19 | b | Consola, Proyectos → detalle | Asignar presupuesto y responsable (`ing.perez`) con justificación | `19-consola-proyecto-asignado.png` |
| 20 | b | Consola (`ing.perez`), detalle del proyecto | Registrar avance y costo, adjuntar un documento | `20-consola-avance-documento.png` |
| 21 | b | Consola, detalle del proyecto | Historial de solo inserción con autor e instante (QA-15) | `21-consola-historial.png` |
| 22 | b | Consola (`alcaldia.armenia`), Consolidado | Consolidado del programa por jurisdicción | `22-consola-consolidado.png` |
| 23 | Transversal | Navegador | Panel de RabbitMQ (`http://localhost:15672`): colas `q.*` con 3 miembros | `23-bus-colas.png` |
| 24 | Transversal | Terminal | Salida de `prueba-qa11.py` (198 de 198 rechazados) y del simulador QA-14 por el gateway | `24-medidas-terminal.png` |

## Qué muestra cada paso frente a la arquitectura

| Pasos | Decisiones y escenarios |
|---|---|
| 1 a 6 | AD-31 y AD-44 (réplica local y cola), AD-04 y AD-22 (gateway y autorización en dos niveles), DA-15 (OIDC), RF-GD-02, RF-GD-03, RF-GD-05, RF-GD-08 |
| 7 a 10 | AD-43 (regla de conflicto), AD-45 (bandeja de salida), QA-14 |
| 11 a 13 | AD-28 (catálogo de tipos), AD-32 y AD-33 (detección sin resolución automática), RF-ED-08 y RF-ED-13 |
| 14 a 17 | AD-15 a AD-17 (modelo de lectura, `datoAl` y caché de geometrías), AD-34, RF-ED-11, RF-ED-15, RF-GD-17 |
| 18 a 22 | AD-40 (proyectos por lote), AD-41 (historial de solo inserción), AD-42 (Consulta del Programa), QA-15 |
| 23 y 24 | AD-02 (broker con colas acotadas), QA-11 y QA-14 medidos |
