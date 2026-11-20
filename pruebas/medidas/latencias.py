"""
Medidas de referencia de QA-06 y QA-07 en compose, a través del gateway y con tokens reales de Keycloak.

No reemplaza la prueba de carga con k6 (10 000 usuarios): mide el camino completo con un perfil local de pocos clientes
concurrentes, para dejar una línea base reproducible.

- QA-06 (consulta frecuente): GET /v1/consulta/desastres/{id} (estado consolidado con datoAl), GET /v1/consulta/mapa y
  GET /v1/consulta-danos/mapa. Reporta p50, p95 y p99 y el porcentaje de respuestas con marca de tiempo del dato.
  También mide la antigüedad del dato: tiempo desde que la escritura confirma una reclasificación hasta que la consulta
  la refleja.
- QA-07 (registro de un evento): POST /v1/desastres con los atributos mínimos. Reporta p50, p95 y p99.

Uso, con la plataforma arriba:
    python -I pruebas/medidas/latencias.py [http://localhost:8080] [--clientes 20] [--consultas 300] [--registros 100]
"""
import argparse
import concurrent.futures
import json
import os
import statistics
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

KEYCLOAK = os.environ.get('KEYCLOAK_URL', 'http://localhost:8180')
POLIGONO = {'type': 'Polygon', 'coordinates': [[[-75.69, 4.53], [-75.67, 4.53], [-75.67, 4.55], [-75.69, 4.55], [-75.69, 4.53]]]}


def token(usuario):
    datos = urllib.parse.urlencode({'grant_type': 'password', 'client_id': 'movil', 'username': usuario,
                                    'password': usuario + '_local'}).encode()
    with urllib.request.urlopen(f'{KEYCLOAK}/realms/plataforma/protocol/openid-connect/token', datos, timeout=10) as r:
        return json.load(r)['access_token']


def pedir(base, metodo, ruta, tok, cuerpo=None):
    datos = json.dumps(cuerpo).encode() if cuerpo is not None else None
    req = urllib.request.Request(base + ruta, data=datos, method=metodo,
                                 headers={'Authorization': 'Bearer ' + tok, 'Content-Type': 'application/json'})
    inicio = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            texto = r.read()
            return r.status, time.perf_counter() - inicio, texto
    except urllib.error.HTTPError as e:
        return e.code, time.perf_counter() - inicio, e.read()
    except (TimeoutError, urllib.error.URLError, ConnectionError):
        # Sin respuesta en 30 s: cuenta como falla y con su tiempo, no se descarta.
        return 0, time.perf_counter() - inicio, b''


def percentil(valores, p):
    orden = sorted(valores)
    k = max(0, min(len(orden) - 1, round(p / 100 * len(orden) + 0.5) - 1))
    return orden[k]


def resumen(nombre, muestras, extra=''):
    tiempos = [t for _, t, _ in muestras]
    ok = sum(1 for c, _, _ in muestras if 200 <= c < 300)
    print(f'| {nombre} | {len(muestras)} | {ok} | {statistics.median(tiempos) * 1000:.0f} | '
          f'{percentil(tiempos, 95) * 1000:.0f} | {percentil(tiempos, 99) * 1000:.0f} | {extra} |')


def registro(nombre):
    return {'tipo': 'INUNDACION', 'nombre': nombre, 'descripcion': 'Medida de QA-07',
            'instanteInicio': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime(time.time() - 3600)),
            'lat': 4.5339, 'lon': -75.6811, 'severidad': 'NIVEL_2', 'responsable': 'ungrd', 'participantes': [],
            'zona': {'geometria': POLIGONO, 'codigosMunicipio': [], 'fuente': 'DECISION_AUTORIDAD'}}


def main():
    a = argparse.ArgumentParser()
    a.add_argument('base', nargs='?', default='http://localhost:8080')
    a.add_argument('--clientes', type=int, default=20)
    a.add_argument('--consultas', type=int, default=300)
    a.add_argument('--registros', type=int, default=100)
    a.add_argument('--muestras-antiguedad', type=int, default=30)
    o = a.parse_args()
    base = o.base.rstrip('/')
    autoridad = token('ungrd.operador')

    # Un desastre propio para consultar.
    c, _, cuerpo = pedir(base, 'POST', '/v1/desastres', autoridad, registro('Medida QA-06'))
    assert c == 201, (c, cuerpo)
    id_desastre = json.loads(cuerpo)['id']
    time.sleep(3)

    print(f'Perfil: {o.clientes} clientes concurrentes contra {base}\n')
    print('| Medida | Peticiones | 2xx | p50 (ms) | p95 (ms) | p99 (ms) | Observación |')
    print('|---|---|---|---|---|---|---|')

    with concurrent.futures.ThreadPoolExecutor(o.clientes) as hilos:
        for ruta in (f'/v1/consulta/desastres/{id_desastre}', '/v1/consulta/mapa',
                     f'/v1/consulta-danos/mapa?desastre={id_desastre}&puntos=true'):
            muestras = list(hilos.map(lambda _: pedir(base, 'GET', ruta, autoridad), range(o.consultas)))
            con_marca = sum(1 for c, _, t in muestras if 200 <= c < 300 and b'datoAl' in t)
            resumen(f'QA-06 GET {ruta.split("?")[0].replace(id_desastre, "{id}")}', muestras,
                    f'marca de tiempo del dato en {con_marca * 100 / len(muestras):.0f} %')

        muestras = list(hilos.map(lambda i: pedir(base, 'POST', '/v1/desastres', autoridad, registro(f'Medida QA-07 {i}')),
                                  range(o.registros)))
        resumen('QA-07 POST /v1/desastres', muestras, 'registro con los atributos mínimos')

    # Antigüedad del dato: reclasificar en la escritura y esperar a que la consulta lo refleje.
    retrasos = []
    niveles = ['NIVEL_3', 'NIVEL_2']
    for i in range(o.muestras_antiguedad):
        nivel = niveles[i % 2]
        c, _, _ = pedir(base, 'PATCH', f'/v1/desastres/{id_desastre}/severidad', autoridad,
                        {'severidad': nivel, 'justificacion': 'Medida de antigüedad del dato'})
        confirmado = time.perf_counter()
        assert c == 200, c
        while True:
            _, _, t = pedir(base, 'GET', f'/v1/consulta/desastres/{id_desastre}', autoridad)
            if nivel.encode() in t:
                retrasos.append(time.perf_counter() - confirmado)
                break
            if time.perf_counter() - confirmado > 30:
                retrasos.append(30.0)
                break
            time.sleep(0.05)
    print(f'| QA-06 antigüedad del dato | {len(retrasos)} | — | {statistics.median(retrasos) * 1000:.0f} | '
          f'{percentil(retrasos, 95) * 1000:.0f} | {max(retrasos) * 1000:.0f} (máx.) | escritura confirmada hasta consulta |')


if __name__ == '__main__':
    main()
