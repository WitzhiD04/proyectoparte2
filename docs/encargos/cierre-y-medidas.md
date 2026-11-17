# Encargo · Cierre: medidas por escenario de calidad, Kubernetes y documentación

Es el último encargo. Lea `docs/encargos/convenciones.md` y `docs/encargos/LEEME.md` para saber qué quedó hecho.

1. **Limpieza:**
   - **Retire de Evaluación de Daños** `GET /v1/mapa-calor` y la página Razor `/mapa`, que son del prototipo. Ya los
     reemplaza Consulta de Daños. Ajuste sus pruebas, las rutas de nginx y el catálogo del gateway.
2. **Medidas.** Corra en compose las pruebas de carga y de caos (`pruebas/carga-k6`, `pruebas/caos`;
   `pruebas/ENCARGO-carga-y-caos.md`), además de las pruebas que ya miden:
   - QA-14 (`EvaluacionDanos.Integracion`);
   - QA-15 (`Auditoria.Integracion`);
   - QA-11 (gateway);
   - QA-06, como referencia (`ConsultaDanos.Integracion`, categoría `Rendimiento`);
   - QA-13 (d): el rastro de 10 000 entradas de Auditoría con p95 < 10 s, con k6 sobre `/v1/auditoria`;
   - QA-14 **a través del gateway** con el simulador (`simuladores/simulador-qa14`), apuntado al puerto 8080 con tokens;
   - la prueba instrumentada de la app (`ContratoServidorTest`, repositorio `danos`) contra el gateway y Keycloak, con el
     emulador y `adb reverse tcp:8180 tcp:8180`.

   Agregue a `docs/decisiones/REVISION_ARQUITECTURA.md` una **tabla de medidas**:

   | Columna | Contenido |
   |---|---|
   | Escenario | El QA |
   | Medida del SRS | Texto de la medida |
   | Cómo se midió | Script o prueba |
   | Resultado | Lo obtenido |
   | ¿Cumple? | Sí o no |
   | Observaciones | Por ejemplo, "perfil local 1/20" o "no medible en una sola máquina" |

   Cada número debe salir de una corrida real; si algo no se pudo medir, se dice.
3. **Kubernetes** (Minikube en WSL, `despliegue/k8s/aplicar.sh`):
   - Verifique que `kubectl kustomize --load-restrictor=LoadRestrictionsNone despliegue/k8s/local` genera todo.
   - Despliegue **solo** en el contexto `minikube`: el script lo exige, y nunca se usa otro contexto.
   - Corrija lo que falle y mida que KEDA escala el adaptador de SE-7 de 0 a N.
4. **Documentación:**
   - El README raíz, con el mapa de servicios y cómo correr todo.
   - La sección "Estado de la implementación" de `REVISION_ARQUITECTURA.md`.
   - En `ENMIENDAS_DOCUMENTOS.md`, las enmiendas que hayan surgido.
5. **Opcional, si hay tiempo:** dos regiones (DA-16, QA-03) en `despliegue/k8s/region-a`, `region-b` y `global`. Si no
   entra, se presenta como diseño.
