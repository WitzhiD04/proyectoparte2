import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { colorIndice, COLOR_NIVEL, geometriaDe, instantesDe, nivelDeIndice, paradaDeVersion, textoCambio } from './formato-mapa';
import { LienzoFalso } from '../../pruebas/apoyo';
import { FABRICA_LIENZO } from './lienzo-mapa';
import { PantallaMapa } from './mapa';
import { CambioDesastre, VersionZona } from './modelos-mapa';

const T1 = '2026-11-16T10:00:00.123456+00:00';
const T2 = '2026-11-16T11:00:00+00:00';
const T3 = '2026-11-16T12:00:00+00:00';
const poligono = (x: number) => ({ type: 'Polygon', coordinates: [[[x, 4], [x + 1, 4], [x + 1, 5], [x, 4]]] });
const distribucion = { SIN_DANO: 0, LEVE: 0, MODERADO: 0, GRAVE: 1, DESTRUCCION_TOTAL: 0 };

const tablero = (cambios = {}) => ({
  datoAl: T3,
  degradado: false,
  datos: [
    { id: 'cerrado', tipo: 'SISMO', nombre: 'Sismo viejo', severidad: 'NIVEL_1', estado: 'CERRADO', zonaVersion: 1, municipios: 1, datoAl: T3 },
    { id: 'd1', tipo: 'SISMO', nombre: 'Sismo del Quindío', severidad: 'NIVEL_3', estado: 'EN_ATENCION', zonaVersion: 2, municipios: 2, datoAl: T2 },
  ],
  ...cambios,
});

const mapaDanos = (cambios = {}) => ({
  type: 'FeatureCollection',
  idDesastre: 'd1',
  datoAl: T3,
  degradado: false,
  resumen: { activos: 1, indiceDano: 0.75, distribucion, conConflicto: 1 },
  features: [
    { type: 'Feature', geometry: poligono(-76), properties: { tipo: 'zona', codigoMunicipio: '63001', nombre: 'Armenia', codigoDepartamento: '63', activos: 1, indiceDano: 0.75, distribucion, conConflicto: 1 } },
    { type: 'Feature', geometry: { type: 'Point', coordinates: [-75.7, 4.5] }, properties: { tipo: 'activo', idActivo: 'a1', idEvaluacion: 'e1', tipoActivo: 'ESCUELA', nivelDano: 'GRAVE', peso: 0.75, validada: true, estadoValidacion: 'VALIDADA', conflictoPendiente: true, codigoMunicipio: '63001', datoAl: T3 } },
  ],
  ...cambios,
});

const mapaZonas = () => ({
  type: 'FeatureCollection',
  datoAl: T3,
  degradado: false,
  features: [
    { type: 'Feature', geometry: poligono(-75), properties: { idDesastre: 'otro', zonaVersion: 9, municipios: [], vigenteDesde: T1, datoAl: T1 } },
    { type: 'Feature', geometry: poligono(-74), properties: { idDesastre: 'd1', zonaVersion: 2, municipios: ['63001', '63130'], vigenteDesde: T3, datoAl: T3 } },
  ],
});

const version = (numero: number, desde: string, x: number): VersionZona => ({
  version: numero, vigenteDesde: desde, geometriaGeoJson: JSON.stringify(poligono(x)), codigosMunicipio: ['63001'], fuente: 'DecisionAutoridad',
});

const cambio = (instante: string, tipo: string, anterior: string | null, nuevo: string): CambioDesastre => ({ instante, tipo, anterior, nuevo, autor: 'ungrd.operador', version: 1 });

const evolucion = (enInstante: unknown = null) => ({
  idDesastre: 'd1',
  cambios: [cambio(T1, 'REGISTRO', null, 'NIVEL_2|REGISTRADO'), cambio(T2, 'SEVERIDAD', 'NIVEL_2', 'NIVEL_3'), cambio(T3, 'ZONA', 'v1', 'v2')],
  zonas: [version(1, T1, -77), version(2, T3, -74)],
  enInstante,
  datoAl: T3,
});

describe('mapa de daños', () => {
  let servidor: HttpTestingController;
  let lienzo: LienzoFalso;
  let fixture: ComponentFixture<PantallaMapa>;
  let html: HTMLElement;

  beforeEach(() => {
    lienzo = new LienzoFalso();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: FABRICA_LIENZO, useValue: () => lienzo }],
    });
    servidor = TestBed.inject(HttpTestingController);
  });

  afterEach(() => servidor.verify());

  const pedido = (ruta: string): TestRequest => servidor.expectOne((r) => r.url === ruta);
  const estable = () => fixture.whenStable();

  async function abrir(): Promise<void> {
    fixture = TestBed.createComponent(PantallaMapa);
    html = fixture.nativeElement as HTMLElement;
    await estable();
  }

  async function responderCapas(danos: object = mapaDanos()): Promise<void> {
    pedido('/v1/consulta-danos/mapa').flush(danos);
    pedido('/v1/consulta/mapa').flush(mapaZonas());
    pedido('/v1/consulta/desastres/d1/evolucion').flush(evolucion());
    await estable();
  }

  async function abrirConDatos(danos: object = mapaDanos()): Promise<void> {
    await abrir();
    pedido('/v1/consulta/desastres').flush(tablero());
    await estable();
    await responderCapas(danos);
  }

  const texto = () => html.textContent ?? '';
  const casilla = (nombre: string) =>
    [...html.querySelectorAll('label')].find((l) => l.textContent?.includes(nombre))!.querySelector('input') as HTMLInputElement;
  const boton = (nombre: string) => [...html.querySelectorAll('button')].find((b) => b.textContent?.includes(nombre)) as HTMLButtonElement;

  async function elegirSelect(id: string, valor: string): Promise<void> {
    const select = html.querySelector(id) as HTMLSelectElement;
    select.value = valor;
    select.dispatchEvent(new Event('change'));
    await estable();
  }

  async function moverBarra(parada: number): Promise<void> {
    const barra = html.querySelector('input[type=range]') as HTMLInputElement;
    barra.value = String(parada);
    barra.dispatchEvent(new Event('input'));
    barra.dispatchEvent(new Event('change'));
    await estable();
  }

  it('abre el desastre en curso más reciente, pinta las tres capas y encuadra una vez', async () => {
    await abrir();
    pedido('/v1/consulta/desastres').flush(tablero());
    await estable();

    const danos = pedido('/v1/consulta-danos/mapa');
    expect(danos.request.params.get('desastre')).toBe('d1');
    expect(danos.request.params.get('puntos')).toBe('true');
    expect(danos.request.params.get('nivelMinimo')).toBe('SIN_DANO');
    expect(danos.request.params.get('soloValidadas')).toBe('false');
    danos.flush(mapaDanos());
    const zonas = pedido('/v1/consulta/mapa');
    expect(zonas.request.params.get('soloActivos')).toBe('false');
    zonas.flush(mapaZonas());
    pedido('/v1/consulta/desastres/d1/evolucion').flush(evolucion());
    await estable();

    expect(lienzo.coropleta.map((z) => z.properties.codigoMunicipio)).toEqual(['63001']);
    expect(lienzo.activos.map((a) => a.properties.idActivo)).toEqual(['a1']);
    expect(lienzo.zona?.version).toBe(2);
    expect(lienzo.anteriores.map((z) => z.version)).toEqual([1]);
    expect(lienzo.encuadres).toBe(1);
    expect(texto()).toContain('1 municipio,');
    expect(texto()).toContain('1 activo, 1 con conflicto pendiente');
    expect(texto()).toContain('Versión 2, 2 municipios');
    expect(html.querySelectorAll('.capa .suave').length).toBe(3);
  });

  it('los filtros vuelven a consultar sin mover el mapa', async () => {
    await abrirConDatos();
    await elegirSelect('#mapa-nivel', 'GRAVE');
    let pedidoDanos = pedido('/v1/consulta-danos/mapa');
    expect(pedidoDanos.request.params.get('nivelMinimo')).toBe('GRAVE');
    pedidoDanos.flush(mapaDanos());
    await estable();

    const validadas = casilla('Solo evaluaciones validadas');
    validadas.checked = true;
    validadas.dispatchEvent(new Event('change'));
    await estable();
    pedidoDanos = pedido('/v1/consulta-danos/mapa');
    expect(pedidoDanos.request.params.get('soloValidadas')).toBe('true');
    pedidoDanos.flush(mapaDanos({ features: [] }));
    await estable();

    expect(lienzo.activos).toEqual([]);
    expect(lienzo.encuadres).toBe(1);
  });

  it('cada capa se apaga y se enciende sin volver a consultar', async () => {
    await abrirConDatos();
    expect(lienzo.visibles).toEqual({ coropleta: true, activos: true, zona: true });

    for (const [nombre, capa] of [['Índice de daño', 'coropleta'], ['Activos evaluados', 'activos'], ['Zona del desastre', 'zona']] as const) {
      const c = casilla(nombre);
      c.checked = false;
      c.dispatchEvent(new Event('change'));
      await estable();
      expect(lienzo.visibles[capa]).toBe(false);
    }
  });

  it('avisa si los daños llegan degradados en el cuerpo', async () => {
    await abrirConDatos(mapaDanos({ degradado: true }));
    const aviso = html.querySelector('.aviso.degradado')!;
    expect(aviso.textContent).toContain('Índice de daño y activos');
    expect(aviso.textContent).toContain('última respuesta conocida');
  });

  it('avisa si la zona llega degradada solo en el encabezado X-Degradado', async () => {
    await abrir();
    pedido('/v1/consulta/desastres').flush(tablero());
    await estable();
    pedido('/v1/consulta-danos/mapa').flush(mapaDanos());
    pedido('/v1/consulta/mapa').flush(mapaZonas(), { headers: { 'X-Degradado': 'true' } });
    pedido('/v1/consulta/desastres/d1/evolucion').flush(evolucion());
    await estable();
    expect(html.querySelector('.aviso.degradado')!.textContent).toContain('Zona del desastre');
  });

  it('sin degradación no muestra el aviso', async () => {
    await abrirConDatos();
    expect(html.querySelector('.aviso.degradado')).toBeNull();
  });

  it('un error muestra el mensaje del servicio y las otras capas siguen', async () => {
    await abrir();
    pedido('/v1/consulta/desastres').flush(tablero());
    await estable();
    pedido('/v1/consulta-danos/mapa').flush({ codigo: 'NO_DISPONIBLE', mensaje: 'Ni la base ni la caché responden.' }, { status: 503, statusText: 'No disponible' });
    pedido('/v1/consulta/mapa').flush(mapaZonas());
    pedido('/v1/consulta/desastres/d1/evolucion').flush(evolucion());
    await estable();
    expect(html.querySelector('.aviso.error')!.textContent).toContain('Ni la base ni la caché responden.');
    expect(lienzo.zona?.version).toBe(2);
    expect(lienzo.encuadres).toBe(1);
  });

  it('sin desastres lo dice y no consulta capas', async () => {
    await abrir();
    pedido('/v1/consulta/desastres').flush(tablero({ datos: [] }));
    await estable();
    expect(html.querySelector('.vacio')!.textContent).toContain('Todavía no hay desastres');
  });

  it('al hacer clic en un activo muestra su evolución y al volver regresan las capas', async () => {
    await abrirConDatos();
    lienzo.alElegir('a1');
    await estable();
    expect(lienzo.marcado).toBe('a1');
    pedido('/v1/consulta-danos/activos/a1/evolucion').flush({
      idActivo: 'a1',
      datoAl: T3,
      vigente: { tipoActivo: 'ESCUELA', nivelDano: 'GRAVE', codigoMunicipio: '63001', estadoValidacion: 'VALIDADA', validada: true, conflictoPendiente: true, fusionado: false },
      evaluaciones: [
        { instante: T1, idEvaluacion: 'e0', versionEvaluacion: 1, origen: 'SINCRONIZACION', nivelDano: 'LEVE', tipoActivo: 'ESCUELA', estadoValidacion: null, enConflicto: false, codigoMunicipio: '63001', autor: 'evaluador.armenia', detalle: null },
        { instante: T2, idEvaluacion: 'e1', versionEvaluacion: 2, origen: 'VALIDACION', nivelDano: 'GRAVE', tipoActivo: 'ESCUELA', estadoValidacion: 'VALIDADA', enConflicto: false, codigoMunicipio: '63001', autor: 'bomberos.operador', detalle: null },
      ],
    });
    await estable();

    expect(html.querySelector('#titulo-activo')!.textContent).toContain('Escuela');
    expect(texto()).toContain('Tiene un conflicto pendiente.');
    expect(html.querySelectorAll('.secuencia li').length).toBe(2);
    expect(texto()).toContain('bomberos.operador');
    expect(html.querySelector('#titulo-capas')).toBeNull();

    boton('Volver a las capas').click();
    await estable();
    expect(html.querySelector('#titulo-capas')).not.toBeNull();
    expect(lienzo.marcado).toBeNull();
  });

  it('si el activo no existe muestra el mensaje del servicio', async () => {
    await abrirConDatos();
    lienzo.alElegir('a1');
    await estable();
    pedido('/v1/consulta-danos/activos/a1/evolucion').flush({ codigo: 'NO_ENCONTRADO', mensaje: 'No hay evaluaciones del activo.' }, { status: 404, statusText: 'No encontrado' });
    await estable();
    expect(html.querySelector('app-panel-activo .aviso.error')!.textContent).toContain('No hay evaluaciones del activo.');
  });

  const zona = {
    zona: { codigo: '63001', nombre: 'ARMENIA', tipo: 'MUNICIPIO' }, idDesastre: 'd1', datoAl: T3,
    evaluaciones: [
      { instante: T1, idActivo: 'a1', tipoActivo: 'ESCUELA', nivelDano: 'LEVE', origen: 'SINCRONIZACION', estadoValidacion: null, autor: 'evaluador.armenia', codigoMunicipio: '63001' },
      { instante: T2, idActivo: 'a1', tipoActivo: 'ESCUELA', nivelDano: 'GRAVE', origen: 'VALIDACION', estadoValidacion: 'VALIDADA', autor: 'bomberos.operador', codigoMunicipio: '63001' },
    ],
    porDia: [{ fecha: '2026-11-16', activos: 1, distribucion }],
  };

  it('al hacer clic en un municipio muestra su evolución en el desastre elegido', async () => {
    await abrirConDatos();
    lienzo.alElegirZona('63001');
    await estable();
    const r = pedido('/v1/consulta-danos/zonas/63001/evolucion');
    expect(r.request.params.get('desastre')).toBe('d1');
    r.flush(zona);
    await estable();
    expect(html.querySelector('#titulo-zona')!.textContent).toContain('ARMENIA');
    expect(texto()).toContain('16/11/2026');
    expect(texto()).toContain('Grave: 1');
    const secuencia = [...html.querySelectorAll('app-panel-zona .secuencia li')].map((l) => l.textContent ?? '');
    expect(secuencia.length).toBe(2);
    expect(secuencia[0]).toContain('Validación');
    boton('Volver a las capas').click();
    await estable();
    expect(html.querySelector('#titulo-capas')).not.toBeNull();
  });

  it('elegir un activo reemplaza el panel del municipio', async () => {
    await abrirConDatos();
    lienzo.alElegirZona('63001');
    await estable();
    pedido('/v1/consulta-danos/zonas/63001/evolucion').flush(zona);
    await estable();
    lienzo.alElegir('a1');
    await estable();
    expect(html.querySelector('app-panel-zona')).toBeNull();
    pedido('/v1/consulta-danos/activos/a1/evolucion').flush({ codigo: 'NO_ENCONTRADO', mensaje: 'x' }, { status: 404, statusText: 'No encontrado' });
    await estable();
  });

  it('la lista de municipios abre la evolución sin usar el mapa y muestra el 404 del servicio', async () => {
    await abrirConDatos();
    (html.querySelector('button[aria-label="Ver evolución de Armenia"]') as HTMLButtonElement).click();
    await estable();
    pedido('/v1/consulta-danos/zonas/63001/evolucion').flush({ codigo: 'NO_ENCONTRADO', mensaje: 'La zona 63001 no tiene evaluaciones.' }, { status: 404, statusText: 'No encontrado' });
    await estable();
    expect(html.querySelector('app-panel-zona .aviso.error')!.textContent).toContain('no tiene evaluaciones');
  });

  it('la lista de activos abre la misma evolución sin usar el mapa', async () => {
    await abrirConDatos();
    ([...html.querySelectorAll('details.activos')].at(-1)!.querySelector('tbody button') as HTMLButtonElement).click();
    await estable();
    expect(lienzo.marcado).toBe('a1');
    pedido('/v1/consulta-danos/activos/a1/evolucion').flush({ codigo: 'NO_ENCONTRADO', mensaje: 'x' }, { status: 404, statusText: 'No encontrado' });
    await estable();
  });

  it('la barra de tiempo consulta el instante exacto y dibuja la zona de ese momento', async () => {
    await abrirConDatos();
    const barra = html.querySelector('input[type=range]') as HTMLInputElement;
    expect(barra.max).toBe('2');
    expect(barra.value).toBe('2');

    await moverBarra(0);
    const consulta = pedido('/v1/consulta/desastres/d1/evolucion');
    expect(consulta.request.params.get('en')).toBe(T1);
    expect(consulta.request.urlWithParams).toContain('%2B00:00');
    consulta.flush(evolucion({ instante: T1, severidad: 'NIVEL_2', estado: 'REGISTRADO', zona: version(1, T1, -77) }));
    await estable();

    expect(lienzo.zona?.version).toBe(1);
    expect(lienzo.anteriores).toEqual([]);
    expect(texto()).toContain('Nivel 2');
    expect(texto()).toContain('Registro: Nivel 2, registrado');
    expect(texto()).toContain('Versión 1, 1 municipio');

    await moverBarra(2);
    expect(lienzo.zona?.version).toBe(2);
    expect(lienzo.anteriores.map((z) => z.version)).toEqual([1]);
    expect(lienzo.encuadres).toBe(1);
  });

  it('un botón de versión lleva la barra al momento en que entró en vigor', async () => {
    await abrirConDatos();
    boton('v1,').click();
    await estable();
    expect(pedido('/v1/consulta/desastres/d1/evolucion').request.params.get('en')).toBe(T1);
  });

  it('al cambiar de desastre limpia el activo elegido y vuelve a encuadrar', async () => {
    await abrirConDatos();
    lienzo.alElegir('a1');
    await estable();
    pedido('/v1/consulta-danos/activos/a1/evolucion').flush({ codigo: 'NO_ENCONTRADO', mensaje: 'x' }, { status: 404, statusText: 'No encontrado' });
    await estable();

    await elegirSelect('#mapa-desastre', 'cerrado');
    expect(lienzo.marcado).toBeNull();
    expect(pedido('/v1/consulta-danos/mapa').request.params.get('desastre')).toBe('cerrado');
    pedido('/v1/consulta/mapa').flush(mapaZonas());
    pedido('/v1/consulta/desastres/cerrado/evolucion').flush({ idDesastre: 'cerrado', cambios: [], zonas: [], enInstante: null, datoAl: null });
    await estable();
    expect(lienzo.zona).toBeNull();
    expect(texto()).toContain('todavía no tiene cambios');
  });

  it('destruye el mapa al salir de la pantalla', async () => {
    await abrir();
    pedido('/v1/consulta/desastres').flush(tablero({ datos: [] }));
    fixture.destroy();
    expect(lienzo.destruido).toBe(true);
  });
});

describe('formato del mapa', () => {
  it('cada índice toma el color del nivel de peso más cercano', () => {
    expect([0, 0.12, 0.13, 0.5, 0.62, 0.63, 1].map(nivelDeIndice)).toEqual(['SIN_DANO', 'SIN_DANO', 'LEVE', 'MODERADO', 'MODERADO', 'GRAVE', 'DESTRUCCION_TOTAL']);
    expect(colorIndice(1)).toBe(COLOR_NIVEL.DESTRUCCION_TOTAL);
    expect(colorIndice(null)).toBe('#c9d2d8');
  });

  it('la rampa va de claro a oscuro', () => {
    const luz = (c: string) => [1, 3, 5].reduce((s, i) => s + parseInt(c.slice(i, i + 2), 16), 0);
    const valores = Object.values(COLOR_NIVEL).map(luz);
    expect(valores).toEqual([...valores].sort((a, b) => b - a));
  });

  it('las paradas son instantes distintos y en orden', () => {
    expect(instantesDe([cambio(T3, 'ZONA', 'v1', 'v2'), cambio(T1, 'REGISTRO', null, 'x'), cambio(T3, 'ESTADO', 'A', 'B')])).toEqual([T1, T3]);
  });

  it('describe cada tipo de cambio en palabras', () => {
    expect(textoCambio(cambio(T1, 'ESTADO', 'EN_ATENCION', 'EN_RECUPERACION'))).toBe('Estado: En atención a En recuperación');
    expect(textoCambio(cambio(T1, 'ZONA', 'v1', 'v2'))).toBe('Zona: v1 a v2');
    expect(textoCambio(cambio(T1, 'ORGANISMOS', null, 'ungrd'))).toBe('Organismos: responsable ungrd');
  });

  it('ubica una versión en su parada', () => {
    expect(paradaDeVersion([T1, T2, T3], version(2, T2, 0))).toBe(1);
    expect(paradaDeVersion([T1, T2], version(3, T3, 0))).toBe(1);
  });

  it('una geometría ausente o dañada no rompe el mapa', () => {
    expect(geometriaDe({ ...version(1, T1, 0), geometriaGeoJson: null })).toBeNull();
    expect(geometriaDe({ ...version(1, T1, 0), geometriaGeoJson: '{roto' })).toBeNull();
  });
});
