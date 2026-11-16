#!/usr/bin/env python3
"""
Prepara datos/geo/municipios-colombia.geojson: la capa de municipios que publica el adaptador de SE-2 (EV-21, DA-07).

Fuentes (datos abiertos del DANE):
  - Geometrías: Marco Geoestadístico Nacional 2018 (MGN2018_MPIO_POLITICO), simplificado con mapshaper por
    github.com/caticoa3/colombia_mapa. Ese archivo trae los nombres con tildes dañados.
  - Nombres y códigos: DIVIPOLA en datos.gov.co (conjunto gdxc-w37w), descargado en CSV, que sí trae UTF-8 correcto.

Salida: un FeatureCollection con, por municipio, las propiedades del contrato
  codigo (DIVIPOLA, 5 dígitos), nombre, codigoDepartamento (2 dígitos) y nombreDepartamento,
y coordenadas redondeadas a 5 decimales (alrededor de 1 m), suficiente para asignar el municipio y para el mapa.

Uso: python -I herramientas/geo/preparar_municipios.py
"""
import csv
import io
import json
import urllib.request
from pathlib import Path

URL_GEOMETRIAS = "https://raw.githubusercontent.com/caticoa3/colombia_mapa/master/co_2018_MGN_MPIO_POLITICO.geojson"
URL_DIVIPOLA = "https://www.datos.gov.co/api/views/gdxc-w37w/rows.csv?accessType=DOWNLOAD"
DESTINO = Path(__file__).resolve().parents[2] / "datos" / "geo" / "municipios-colombia.geojson"


def descargar(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=120) as respuesta:
        return respuesta.read()


def redondear(coordenadas):
    if isinstance(coordenadas[0], (int, float)):
        return [round(coordenadas[0], 5), round(coordenadas[1], 5)]
    return [redondear(c) for c in coordenadas]


def main():
    geometrias = json.loads(descargar(URL_GEOMETRIAS).decode("utf-8"))
    filas = csv.DictReader(io.StringIO(descargar(URL_DIVIPOLA).decode("utf-8-sig")))
    divipola = {}
    # Columnas del CSV: Código Departamento, Nombre Departamento, Código Municipio, Nombre Municipio, ...
    for fila in filas:
        divipola[fila["Código Municipio"].strip().zfill(5)] = {
            "nom_mpio": fila["Nombre Municipio"],
            "dpto": fila["Nombre Departamento"],
        }

    municipios = []
    faltantes = []
    for f in geometrias["features"]:
        codigo = f["properties"]["MPIO_CCNCT"].zfill(5)
        datos = divipola.get(codigo)
        if datos is None:
            # División del MGN 2018 que ya no está en la DIVIPOLA vigente (por ejemplo 94663, Mapiripana). Se conserva
            # con el nombre del MGN, que no tiene tildes dañadas en estos casos.
            faltantes.append(codigo)
            datos = {"nom_mpio": f["properties"]["MPIO_CNMBR"], "dpto": divipola.get(codigo[:2] + "001", {}).get("dpto", "")}
        municipios.append({
            "type": "Feature",
            "properties": {
                "codigo": codigo,
                "nombre": datos["nom_mpio"].strip(),
                "codigoDepartamento": codigo[:2],
                "nombreDepartamento": datos["dpto"].strip(),
            },
            "geometry": {"type": f["geometry"]["type"], "coordinates": redondear(f["geometry"]["coordinates"])},
        })
    municipios.sort(key=lambda m: m["properties"]["codigo"])
    if any("�" in m["properties"]["nombre"] for m in municipios):
        raise SystemExit("Hay nombres con caracteres dañados: revise la fuente DIVIPOLA.")

    DESTINO.parent.mkdir(parents=True, exist_ok=True)
    coleccion = {
        "type": "FeatureCollection",
        "name": "municipios-colombia",
        "fuente": "DANE: MGN 2018 (geometrías simplificadas) y DIVIPOLA (nombres y códigos)",
        "features": municipios,
    }
    DESTINO.write_text(json.dumps(coleccion, ensure_ascii=False, separators=(",", ":")), encoding="utf-8", newline="\n")
    print(f"{DESTINO}: {len(municipios)} municipios; sin nombre DIVIPOLA: {faltantes or 'ninguno'}")


if __name__ == "__main__":
    main()
