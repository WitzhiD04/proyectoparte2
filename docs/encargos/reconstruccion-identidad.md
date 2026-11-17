# Encargo · Reconstrucción con la identidad común y la jurisdicción territorial (DA-14)

Lea primero `docs/encargos/convenciones.md`. Alcance: solo `servicios/reconstruccion`, junto con las pruebas de humo y
la configuración de compose y k8s que le corresponden.

## Qué falta

Reconstrucción es el único servicio que todavía lee la identidad a su manera. Eso incluye su segundo rol, la Consulta
del Programa (`consulta-programa` en compose: la misma imagen con `Reconstruccion__Rol=consulta`), que también debe
pasar a `IIdentidadActual` y recibir las variables `Identidad__*` en compose y k8s:
- encabezados `X-Usuario`, `X-Rol` (`Autoridad`, `Responsable`, `Auditor`) y `X-Ambito`;
- el armado está en `ControladorBase` o similar, en `src/Reconstruccion.Api`.

Los demás usan `IIdentidadActual` de `comun/Plataforma.Comun.Seguridad`. Con ese puerto, la identidad sale del JWT o de
los encabezados según `Identidad:Modo`, sin tocar el código.

1. **Pase Reconstrucción a `IIdentidadActual`** (`AgregarIdentidad` en `Program.cs`) y traduzca la identidad al `Actor`
   del dominio.
   - Roles del realm: `autoridad` → Autoridad, `responsable-proyecto` → Responsable, `auditor` → Auditor.
   - Del claim `ambito` sale el ámbito de la autoridad.
2. **Aplique DA-14** (`docs/decisiones/REVISION_ARQUITECTURA.md`), que es el refinamiento de la regla del Diseño RC "lo
   aprueba la autoridad con jurisdicción sobre el proyecto":

   | Alcance del proyecto | Municipios | Lo aprueba |
   |---|---|---|
   | `Municipal` | uno | La autoridad `LOCAL` cuyo claim `territorio` es **ese** municipio (código DIVIPOLA de 5 dígitos) |
   | `Regional` | varios del mismo departamento | La autoridad `REGIONAL` de **ese** departamento (`territorio` de 2 dígitos) |
   | `Nacional` | de varios departamentos | La autoridad `NACIONAL`. Es un valor nuevo de `AlcanceTerritorial`; el canónico ya existe en el contrato (`AlcanceProyecto`) |

   - Revise la regla actual en `ProyectoReconstruccion.Aprobar` y `CreacionDeProyectos`.
   - Mire también la consulta del consolidado: una autoridad solo ve lo de su territorio. Si eso no está en las fuentes,
     no lo invente: repórtelo.
3. **Pruebas.**
   - Ajuste las existentes (unas 70 entre dominio, casos de uso e integración) a los roles y al territorio.
   - Agregue pruebas de dominio de DA-14: aprueba la autoridad local de otro municipio (403), aprueba la regional del
     departamento (ok), un proyecto de varios departamentos lo aprueba solo la nacional, etc.
4. **Pruebas de humo.** En `pruebas/humo/prueba-humo.sh`, Reconstrucción deja de usar `X-Rol` y usa la misma identidad
   que los demás. Los usuarios de prueba tienen `territorio` en Keycloak; si a alguno le falta, agréguelo en
   `despliegue/keycloak/realm-plataforma.json`.

## Criterio de salida

- `bash herramientas/dotnet.sh test Plataforma.sln` en verde.
- La prueba de humo del escenario E pasa en los dos modos de identidad.
- Reporte final con el formato de `convenciones.md`.
