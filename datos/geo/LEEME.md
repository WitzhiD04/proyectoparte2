# Datos geográficos

`municipios-colombia.geojson` es la capa de municipios que el adaptador de SE-2 publica en EV-21 (DA-07, E-11).
Evaluación de Daños la usa para asignar el municipio de cada activo, y las consultas la usan para el mapa por zonas.

- **Propiedades de cada municipio:** `codigo` (DIVIPOLA, 5 dígitos), `nombre`, `codigoDepartamento` y `nombreDepartamento`.
- **Geometrías:** Polygon o MultiPolygon en WGS 84, con las coordenadas redondeadas a 5 decimales.
- **Se regenera con** `python -I herramientas/geo/preparar_municipios.py`.

## Fuentes y atribución

- **Geometrías:** Marco Geoestadístico Nacional 2018 del DANE (MGN2018_MPIO_POLITICO), simplificado con mapshaper en
  [caticoa3/colombia_mapa](https://github.com/caticoa3/colombia_mapa).
- **Nombres y códigos:** DIVIPOLA del DANE, publicada en datos.gov.co (conjunto `gdxc-w37w`).
- **Licencia:** son datos abiertos del Estado colombiano. **Hay que verificar la licencia exacta del DANE y citarla en la
  entrega** (aviso del plan).

## Límites conocidos

- Es la división de 2018. Un área que cambió después aparece con su código y su nombre de 2018. Hoy es el caso de
  94663 (Mapiripana, Guainía), que ya no está en la DIVIPOLA vigente.
- La simplificación basta para el mapa y para asignar el municipio. En las fronteras entre municipios puede haber
  diferencias de algunos metros.
