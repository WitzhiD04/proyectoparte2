"""
QA-11 medido (DA-12, DA-15): recorre cada ruta de modificación del catálogo del gateway que declara una regla de dominio
y, con tokens reales de Keycloak, la intenta con actores que NO deben poder hacerla: el organismo no responsable
(bomberos.operador frente a un desastre cuyo responsable es ungrd) y lo equivalente en cada servicio (otra alcaldía, otro
dispositivo, un responsable no asignado...). Verifica que el 100 % se rechace, que ningún dato cambie, e imprime la tabla
ruta -> código, con el nivel que rechazó (gateway = nivel 1, servicio = nivel 2).

Uso, con la plataforma arriba (compose, IDENTIDAD_MODO=Jwt):
    python -I pruebas/humo/prueba-qa11.py [http://localhost:8080]
"""
import json
import os
import random
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://localhost:8080').rstrip('/') + '/v1'
KEYCLOAK = os.environ.get('KEYCLOAK_URL', 'http://localhost:8180')
RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CATALOGO = os.path.join(RAIZ, 'gateway', 'src', 'Plataforma.Gateway', 'rutas.json')
MENSAJE_NIVEL_1 = 'Su rol no permite esta operación.'

USUARIOS = ['ungrd.operador', 'bomberos.operador', 'evaluador.armenia', 'operador.danos', 'admin.catalogo', 'auditor.1',
            'ing.perez', 'ing.gomez', 'alcaldia.armenia', 'alcaldia.calarca', 'gobernacion.quindio']


def token(usuario):
    datos = urllib.parse.urlencode({'grant_type': 'password', 'client_id': 'movil', 'username': usuario,
                                    'password': usuario + '_local'}).encode()
    with urllib.request.urlopen(f'{KEYCLOAK}/realms/plataforma/protocol/openid-connect/token', datos, timeout=10) as r:
        return json.load(r)['access_token']


TOKENS = {}


def pedir(metodo, ruta, usuario=None, cuerpo=None, encabezados=None, multipart=None):
    """Devuelve (código, cuerpo JSON o texto)."""
    h = dict(encabezados or {})
    if usuario:
        h['Authorization'] = 'Bearer ' + TOKENS[usuario]
    datos = None
    if multipart is not None:
        limite = 'qa11' + uuid.uuid4().hex
        nombre, contenido = multipart
        datos = (f'--{limite}\r\nContent-Disposition: form-data; name="archivo"; filename="{nombre}"\r\n'
                 f'Content-Type: text/plain\r\n\r\n').encode() + contenido + f'\r\n--{limite}--\r\n'.encode()
        h['Content-Type'] = f'multipart/form-data; boundary={limite}'
    elif cuerpo is not None:
        datos = json.dumps(cuerpo).encode()
        h['Content-Type'] = 'application/json'
    peticion = urllib.request.Request(BASE + ruta, data=datos, method=metodo, headers=h)
    try:
        with urllib.request.urlopen(peticion, timeout=30) as r:
            codigo, texto = r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        codigo, texto = e.code, e.read().decode()
    try:
        return codigo, json.loads(texto) if texto else None
    except json.JSONDecodeError:
        return codigo, texto


def exigir(descripcion, esperado, resultado):
    codigo, cuerpo = resultado
    if codigo != esperado:
        sys.exit(f'Preparación fallida: {descripcion}: esperaba {esperado} y llegó {codigo} -> {str(cuerpo)[:300]}')
    print(f'  ok   {descripcion} ({codigo})')
    return cuerpo


def hasta(descripcion, condicion, metodo, ruta, usuario, encabezados=None, segundos=40):
    for _ in range(segundos * 2):
        codigo, cuerpo = pedir(metodo, ruta, usuario, encabezados=encabezados)
        try:
            if codigo == 200 and condicion(cuerpo):
                print(f'  ok   {descripcion}')
                return cuerpo
        except (KeyError, TypeError, IndexError):
            pass
        time.sleep(0.5)
    sys.exit(f'Preparación fallida: {descripcion} -> {codigo} {str(cuerpo)[:300]}')


def preparar():
    """El escenario mínimo: un desastre de ungrd en Armenia, una evaluación validada y un proyecto de Armenia."""
    print('Preparación (con los actores autorizados)')
    zona = {'geometria': {'type': 'Polygon', 'coordinates': [[[-75.72, 4.50], [-75.64, 4.50], [-75.64, 4.58],
                                                              [-75.72, 4.58], [-75.72, 4.50]]]},
            'fuente': 'DECISION_AUTORIDAD'}
    d = exigir('ungrd registra el desastre (responsable: ungrd; participa bomberos)', 201, pedir('POST', '/desastres', 'ungrd.operador', {
        'tipo': 'TERREMOTO', 'nombre': 'Sismo QA-11', 'descripcion': 'Banco de prueba de QA-11',
        'instanteInicio': '2026-11-17T08:00:00Z', 'lat': 4.5339, 'lon': -75.6811, 'severidad': 'NIVEL_3',
        'responsable': 'ungrd', 'participantes': [{'organismo': 'bomberos', 'responsabilidad': 'Evaluación de daños'}],
        'zona': zona}))['id']
    dispositivo = 'qa11-' + uuid.uuid4().hex[:8]
    eva = {'X-Dispositivo': dispositivo}
    hasta('Evaluación de Daños conoce el desastre', lambda c: any(x['id'] == d for x in c['desastres']),
          'GET', f'/descarga?desastreId={d}', 'evaluador.armenia', eva)
    ev, activo = str(uuid.uuid4()), str(uuid.uuid4())
    lat, lon = round(4.515 + random.random() * 0.02, 6), round(-75.70 + random.random() * 0.03, 6)
    instante = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
    exigir('el evaluador sincroniza una evaluación grave', 200, pedir('POST', '/sincronizacion/evaluaciones', 'evaluador.armenia', {
        'idDispositivo': dispositivo, 'instanteEnvioDispositivo': instante, 'operaciones': [{
            'idOperacion': str(uuid.uuid4()), 'tipo': 'CREAR', 'evaluacion': {
                'id': ev, 'activoId': activo, 'activoNuevo': {'id': activo, 'tipoCodigo': 'escuela', 'lat': lat, 'lon': lon,
                                                              'referencia': 'Escuela QA-11'},
                'desastreId': d, 'instanteCampo': instante, 'evaluador': 'evaluador.armenia', 'organizacion': 'alcaldia-armenia',
                'lat': lat, 'lon': lon, 'precisionM': 5, 'tipoActivoCodigo': 'escuela', 'nivelDano': 'GRAVE',
                'descripcion': 'QA-11', 'evidencias': []}}]}, eva))
    for _ in range(60):
        codigo, cuerpo = pedir('POST', f'/evaluaciones/{ev}/validacion', 'bomberos.operador',
                               {'estado': 'VALIDADA', 'motivo': 'Verificada'})
        if codigo == 200:
            break
        time.sleep(0.5)
    print(f'  ok   bomberos (participante) valida la evaluación ({codigo})')
    for estado in ['EN_ATENCION', 'EN_RECUPERACION', 'EN_RECONSTRUCCION']:
        exigir(f'ungrd pasa el desastre a {estado}', 200, pedir('POST', f'/desastres/{d}/transiciones', 'ungrd.operador',
                                                              {'estado': estado}))
    hasta('la evaluación es candidata en Reconstrucción', lambda c: any(x['idEvaluacion'] == ev for x in c),
          'GET', f'/reconstruccion/candidatas?desastre={d}', 'alcaldia.armenia')
    p = exigir('la alcaldía de Armenia crea el proyecto', 201, pedir('POST', '/proyectos:lote', 'alcaldia.armenia', {
        'idDesastre': d, 'idsEvaluaciones': [ev], 'claveIdempotencia': 'qa11-' + uuid.uuid4().hex}))['idsProyectos'][0]
    exigir('presupuesto', 200, pedir('PATCH', f'/proyectos/{p}/presupuesto', 'alcaldia.armenia', {
        'presupuesto': 500000000, 'fuenteFinanciacion': 'Fondo', 'inicio': '2027-01-15', 'fin': '2027-09-30',
        'justificacion': 'Inicial'}))
    exigir('responsable ing.perez', 200, pedir('PATCH', f'/proyectos/{p}/responsable', 'alcaldia.armenia', {
        'responsable': 'ing.perez', 'justificacion': 'Inicial'}))
    return d, ev, p, dispositivo


def foto(d, ev, p, dispositivo):
    """Los datos que un rechazo no debe cambiar (QA-11 b)."""
    _, desastre = pedir('GET', f'/desastres/{d}', 'ungrd.operador')
    _, evaluaciones = pedir('GET', f'/evaluaciones?idDispositivo={dispositivo}', 'evaluador.armenia')
    if isinstance(evaluaciones, dict):
        evaluaciones = evaluaciones.get('evaluaciones', [])
    evaluacion = next((e for e in (evaluaciones or []) if str(e.get('id')) == ev), {})
    _, proyecto = pedir('GET', f'/proyectos/{p}', 'alcaldia.armenia')
    datos = {
        'desastre': {k: desastre.get(k) for k in ('severidad', 'estado', 'responsable')},
        'evaluacion': {k: evaluacion.get(k) for k in ('estado', 'nivelDano', 'descripcion', 'versiones')},
        'proyecto': {k: proyecto.get(k) for k in ('presupuesto', 'fuenteFinanciacion', 'responsable', 'estado', 'cronograma')},
    }
    # Una foto vacía haría pasar la comparación sin verificar nada.
    for nombre, valores in datos.items():
        if all(v is None for v in valores.values()):
            sys.exit(f'No se pudo leer {nombre} para verificar que no cambie: {valores}')
    return datos


def casos(d, ev, p, dispositivo):
    """(ruta del catálogo, método, ruta, actor, cuerpo, encabezados, multipart). Cada actor NO debe poder hacerlo."""
    todos_menos = lambda *permitidos: [u for u in USUARIOS if u not in permitidos]
    zona = {'geometria': {'type': 'Polygon', 'coordinates': [[[-75.70, 4.52], [-75.66, 4.52], [-75.66, 4.56],
                                                              [-75.70, 4.56], [-75.70, 4.52]]]},
            'fuente': 'DECISION_AUTORIDAD', 'justificacion': 'QA-11'}
    lista = []

    def agregar(rid, metodo, ruta, actores, cuerpos, encabezados=None, multipart=None):
        for actor in actores:
            for cuerpo in cuerpos:
                lista.append((rid, metodo, ruta, actor, cuerpo, encabezados, multipart))

    # Gestión de Desastres: solo el organismo responsable (ungrd) modifica el desastre (QA-11, Cuadro 34).
    no_responsables = todos_menos('ungrd.operador')
    agregar('gd-severidad', 'PATCH', f'/desastres/{d}/severidad', no_responsables,
            [{'severidad': 'NIVEL_4', 'justificacion': 'QA-11'}, {'severidad': 'NIVEL_1', 'justificacion': 'QA-11'}])
    agregar('gd-zona', 'PUT', f'/desastres/{d}/zona', no_responsables, [zona])
    agregar('gd-transiciones', 'POST', f'/desastres/{d}/transiciones', no_responsables,
            [{'estado': 'CERRADO', 'justificacion': 'QA-11'}, {'estado': 'EN_ATENCION', 'justificacion': 'QA-11'}])
    agregar('gd-organismos-asignar', 'PUT', f'/desastres/{d}/organismos', no_responsables,
            [{'responsable': 'bomberos', 'participantes': [], 'justificacion': 'QA-11'}])
    # Evaluación de Daños: la evaluación la actualiza el dispositivo que la registró (o un operador); la valida un
    # organismo que participa en el desastre.
    cambios = [{'nivelDano': 'LEVE'}, {'descripcion': 'Cambiada por QA-11'}]
    agregar('ed-actualizar', 'PATCH', f'/evaluaciones/{ev}', todos_menos('evaluador.armenia', 'operador.danos'), cambios)
    agregar('ed-actualizar', 'PATCH', f'/evaluaciones/{ev}', ['evaluador.armenia'], cambios, {'X-Dispositivo': 'otro-dispositivo'})
    agregar('ed-validacion', 'POST', f'/evaluaciones/{ev}/validacion', todos_menos('bomberos.operador', 'ungrd.operador'),
            [{'estado': 'RECHAZADA', 'motivo': 'QA-11'}, {'estado': 'PENDIENTE'}])
    # Reconstrucción: jurisdicción territorial (DA-14, E-39) y responsable asignado.
    sin_jurisdiccion = todos_menos('alcaldia.armenia', 'gobernacion.quindio', 'ungrd.operador')
    agregar('rc-lote', 'POST', '/proyectos:lote', sin_jurisdiccion,
            [{'idDesastre': d, 'idsEvaluaciones': [ev], 'claveIdempotencia': 'qa11-' + uuid.uuid4().hex}])
    agregar('rc-presupuesto', 'PATCH', f'/proyectos/{p}/presupuesto', sin_jurisdiccion,
            [{'presupuesto': 1, 'justificacion': 'QA-11'}, {'fuenteFinanciacion': 'Otra', 'justificacion': 'QA-11'}])
    agregar('rc-responsable', 'PATCH', f'/proyectos/{p}/responsable', sin_jurisdiccion,
            [{'responsable': 'ing.gomez', 'justificacion': 'QA-11'}])
    agregar('rc-transiciones', 'POST', f'/proyectos/{p}/transiciones', sin_jurisdiccion,
            [{'accion': 'CANCELAR', 'justificacion': 'QA-11'}])
    # Aprobar un municipal de Armenia: solo la alcaldía de Armenia (la gobernación y la nacional lo ven, pero no lo aprueban).
    agregar('rc-transiciones', 'POST', f'/proyectos/{p}/transiciones', todos_menos('alcaldia.armenia'), [{'accion': 'APROBAR'}])
    no_asignados = todos_menos('ing.perez')
    agregar('rc-transiciones', 'POST', f'/proyectos/{p}/transiciones', no_asignados,
            [{'accion': 'INICIAR', 'contratista': 'QA-11'}, {'accion': 'FINALIZAR'}])
    agregar('rc-avance', 'POST', f'/proyectos/{p}/avance', no_asignados,
            [{'avance': 10}, {'costo': 1000, 'concepto': 'QA-11'}])
    agregar('rc-documentos', 'POST', f'/proyectos/{p}/documentos', no_asignados, [None], None, ('qa11.txt', b'QA-11'))
    return lista


def main():
    for u in USUARIOS:
        TOKENS[u] = token(u)
    with open(CATALOGO, encoding='utf-8') as f:
        rutas = json.load(f)['ReverseProxy']['Routes']
    con_regla = {rid for rid, r in rutas.items()
                 if any(m in ('POST', 'PUT', 'PATCH', 'DELETE') for m in r['Match']['Methods'])
                 and r['Metadata'].get('reglaDominio', 'ninguna') != 'ninguna'}

    d, ev, p, dispositivo = preparar()
    antes = foto(d, ev, p, dispositivo)
    lista = casos(d, ev, p, dispositivo)
    faltan = con_regla - {c[0] for c in lista}
    if faltan:
        sys.exit(f'El banco de QA-11 no cubre estas rutas con regla de dominio: {sorted(faltan)}')

    print(f'\nQA-11: {len(lista)} intentos de modificación por actores no autorizados, sobre {len(con_regla)} rutas con regla')
    filas, falsos_negativos = [], []
    for rid, metodo, ruta, actor, cuerpo, encabezados, multipart in lista:
        codigo, respuesta = pedir(metodo, ruta, actor, cuerpo, encabezados, multipart)
        nivel = 'gateway (1)' if isinstance(respuesta, dict) and respuesta.get('mensaje') == MENSAJE_NIVEL_1 else 'servicio (2)'
        rechazado = codigo in (401, 403, 404)
        if not rechazado:
            falsos_negativos.append((rid, metodo, actor, codigo, respuesta))
        if isinstance(cuerpo, dict):
            detalle = cuerpo.get('accion') or cuerpo.get('estado') or cuerpo.get('severidad') or ', '.join(
                k for k in cuerpo if k != 'justificacion')
        else:
            detalle = 'archivo'
        filas.append((rid, metodo, rutas[rid]['Match']['Path'], rutas[rid]['Metadata']['reglaDominio'], detalle, actor,
                      encabezados.get('X-Dispositivo', '') if encabezados else '', codigo, nivel))
    despues = foto(d, ev, p, dispositivo)

    print('\n| Ruta | Método | Regla de dominio | Operación | Actor | Código | Rechazó |')
    print('|---|---|---|---|---|---|---|')
    for rid, metodo, path, regla, detalle, actor, disp, codigo, nivel in filas:
        actor_txt = f'{actor} ({disp})' if disp else actor
        print(f'| `{path}` | {metodo} | {regla} | {detalle} | {actor_txt} | {codigo} | {nivel} |')

    resumen = {}
    for f in filas:
        resumen.setdefault(f[0], {}).setdefault(f[7], 0)
        resumen[f[0]][f[7]] += 1
    print('\n| Ruta del catálogo | Intentos | Códigos |')
    print('|---|---|---|')
    for rid in sorted(resumen):
        total = sum(resumen[rid].values())
        codigos = ', '.join(f'{c}: {n}' for c, n in sorted(resumen[rid].items()))
        print(f'| {rid} | {total} | {codigos} |')

    print(f'\nRechazados: {len(lista) - len(falsos_negativos)} de {len(lista)} '
          f'({100.0 * (len(lista) - len(falsos_negativos)) / len(lista):.1f} %). Falsos negativos: {len(falsos_negativos)}.')
    print(f'Datos modificados en los casos rechazados: {"0" if antes == despues else "SÍ"}')
    if antes != despues:
        print('  antes:  ', antes)
        print('  después:', despues)
    for f in falsos_negativos:
        print('  FALLA (no se rechazó):', f)
    sys.exit(0 if not falsos_negativos and antes == despues else 1)


if __name__ == '__main__':
    main()
