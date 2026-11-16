# Adaptador de SE-2 (capas geográficas)

Entrega a la plataforma la capa de municipios (`datos/geo/municipios-colombia.geojson`) como **EV-21**, "Capa
geográfica actualizada" (AD-03, DA-07, E-11). Aísla el formato del sistema externo: los servicios solo conocen el
evento canónico de `comun` (AD-09). Evaluación de Daños la consume para asignar el municipio de cada activo.

- **Por partes:** un EV-21 por departamento (33), con `parte` y `total`.
- **Versión:** los primeros 31 bits del SHA-256 del archivo. No es un consecutivo: el mismo archivo da siempre la misma
  versión y las mismas claves de idempotencia (`capa:MUNICIPIOS:v<version>:<parte>`), aunque el adaptador pierda su base.
- **No republica:** su base `adaptador_se2` guarda el registro de capas publicadas y la bandeja de salida. El registro
  y las 33 partes se guardan en una sola transacción (AD-45); el relevo de `comun` las entrega al bus.
- **Capa nueva:** se reemplaza el archivo (la imagen lo trae en `/app/datos/geo`). El adaptador lo revisa al arrancar y
  cada `Capa__IntervaloRevisionSegundos` (300 por omisión).

Es un trabajador pequeño en un solo proyecto: `Capas.cs` (lectura y traducción, sin base ni bus), `Persistencia.cs`,
`PublicadorCapas.cs` y `Program.cs`.

## Configuración (variables de entorno; no tiene `appsettings.json`)

| Variable | Uso |
|---|---|
| `ConnectionStrings__AdaptadorSe2` | Base del adaptador, con el rol `adaptador_se2_app` |
| `Bus__Nodos__0..n`, `Bus__Clave` | Bus. El usuario y el productor son `adaptador-se2` |
| `Capa__Ruta` | Archivo de la capa (por omisión `datos/geo/municipios-colombia.geojson`) |
| `ConnectionStrings__AdaptadorSe2Dueno`, `Migracion__ClaveAdaptadorSe2App` | Solo para `migrar` |

## Probar

```bash
bash herramientas/dotnet.sh test adaptadores/adaptador-se2/tests/AdaptadorSe2.Pruebas
```
