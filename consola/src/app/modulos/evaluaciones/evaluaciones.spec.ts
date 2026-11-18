import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RolRealm } from '../../nucleo/sesion';
import { OidcFalso, iniciarSesionComo, proveedoresOidc, tokenDe } from '../../pruebas/apoyo';
import { Catalogo } from './catalogo';
import { Conflictos } from './conflictos';
import { RUTAS_EVALUACIONES } from './evaluaciones.routes';
import { compararVersiones, razonNoDisponible } from './formato-evaluaciones';
import { Conflicto, VersionEvaluacion } from './modelos-evaluaciones';
import { Validacion } from './validacion';

const T = '2026-11-18T18:44:21.920292Z';
const distribucion = { SIN_DANO: 0, LEVE: 1, MODERADO: 0, GRAVE: 1, DESTRUCCION_TOTAL: 0 };

const activo = (idEvaluacion: string, tipoActivo: string, nivelDano: string, peso: number, estadoValidacion: string | null, conflictoPendiente = false) => ({
  type: 'Feature',
  geometry: { type: 'Point', coordinates: [-75.7, 4.5] },
  properties: {
    tipo: 'activo', idActivo: `a-${idEvaluacion}`, idEvaluacion, tipoActivo, nivelDano, peso, validada: estadoValidacion === 'VALIDADA',
    estadoValidacion, conflictoPendiente, codigoMunicipio: '63001', datoAl: T,
  },
});

const mapa = (features: object[] = [activo('e1', 'ESCUELA', 'GRAVE', 0.75, null), activo('e2', 'VIVIENDA', 'LEVE', 0.25, 'VALIDADA'), activo('e3', 'PUENTE', 'GRAVE', 0.75, null, true)]) => ({
  type: 'FeatureCollection',
  idDesastre: 'd1',
  datoAl: T,
  degradado: false,
  resumen: { activos: 3, indiceDano: 0.5, distribucion, conConflicto: 1 },
  features: [
    { type: 'Feature', geometry: null, properties: { tipo: 'zona', codigoMunicipio: '63001', nombre: 'ARMENIA', codigoDepartamento: '63', activos: 3, indiceDano: 0.5, distribucion, conConflicto: 1 } },
    ...features,
  ],
});

const version = (numeroVersion: number, nivelDano: string, cambios: Partial<VersionEvaluacion> = {}): VersionEvaluacion => ({
  versionId: `v${numeroVersion}`, evaluacionId: `e${numeroVersion}`, numeroVersion, autor: 'evaluador.armenia', idDispositivo: 'tableta-1', enConflicto: false, instante: T,
  contenido: { id: `e${numeroVersion}`, nivelDano, tipoActivoCodigo: 'escuela', descripcion: 'Muro caído', lat: 4.5, lon: -75.7, evidencias: [{}], organizacion: 'alcaldia-armenia' },
  motivo: 'SINCRONIZACION',
  ...cambios,
});

const conflicto = (cambios: Partial<Conflicto> = {}): Conflicto => ({
  id: 'c1', activoId: 'a1b2c3d4-0000', tipo: 'DUPLICADO', estado: 'PENDIENTE', regla: 'Posible duplicado a 6 m', instante: T,
  vigente: version(2, 'GRAVE'), entrante: version(1, 'MODERADO', { enConflicto: true }), ...cambios,
});

describe('módulo de evaluaciones', () => {
  let servidor: HttpTestingController;
  let fixture: ComponentFixture<unknown>;
  let html: HTMLElement;
  let oidc: OidcFalso;

  beforeEach(() => {
    oidc = new OidcFalso();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'evaluaciones', children: RUTAS_EVALUACIONES }]), provideHttpClient(), provideHttpClientTesting(), ...proveedoresOidc(oidc)],
    });
    servidor = TestBed.inject(HttpTestingController);
  });

  afterEach(() => servidor.verify());

  const pedido = (url: string): TestRequest => servidor.expectOne((r) => r.url === url);
  const estable = () => fixture.whenStable();
  const texto = () => html.textContent ?? '';
  const boton = (nombre: string) => [...html.querySelectorAll('button')].find((b) => b.textContent?.trim() === nombre) as HTMLButtonElement;
  const filas = () => [...html.querySelectorAll('tbody tr')].map((f) => f.textContent ?? '');

  async function abrir<T>(componente: Type<T>): Promise<void> {
    fixture = TestBed.createComponent(componente);
    html = fixture.nativeElement as HTMLElement;
    await estable();
  }

  async function escribir(selector: string, valor: string): Promise<void> {
    const campo = html.querySelector(selector) as HTMLInputElement;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await estable();
  }

  async function pulsar(nombre: string): Promise<void> {
    boton(nombre).click();
    await estable();
  }

  describe('validación', () => {
    async function abrirValidacion(datos: object = mapa()): Promise<void> {
      await abrir(Validacion);
      (fixture.componentInstance as Validacion).esperaActualizacion = 0;
      pedido('/v1/consulta/desastres').flush({ datoAl: T, degradado: false, datos: [{ id: 'cerrado', nombre: 'Viejo', estado: 'CERRADO', datoAl: T }, { id: 'd1', nombre: 'Sismo del Quindío', estado: 'EN_ATENCION', datoAl: T }] });
      await estable();
      const lista = pedido('/v1/consulta-danos/mapa');
      expect(lista.request.params.get('desastre')).toBe('d1');
      expect(lista.request.params.get('soloValidadas')).toBe('false');
      lista.flush(datos);
      await estable();
    }

    const revisar = async (tipo: string) => {
      const fila = [...html.querySelectorAll('tbody tr')].find((f) => f.textContent?.includes(tipo))!;
      (fila.querySelector('button') as HTMLButtonElement).click();
      await estable();
    };

    it('lista primero lo que falta validar, con su municipio y nivel', async () => {
      await abrirValidacion();
      expect(filas().length).toBe(2);
      expect(filas()[0]).toContain('Escuela');
      expect(filas()[0]).toContain('ARMENIA');
      expect(filas()[0]).toContain('Grave');
      expect(texto()).not.toContain('Vivienda');
    });

    it('el filtro muestra las validadas', async () => {
      await abrirValidacion();
      const filtro = html.querySelector('#val-estado') as HTMLSelectElement;
      filtro.value = 'VALIDADAS';
      filtro.dispatchEvent(new Event('change'));
      await estable();
      expect(filas().length).toBe(1);
      expect(filas()[0]).toContain('Vivienda');
    });

    it('valida, confirma y saca la fila aunque el modelo de lectura aún no se entere', async () => {
      await abrirValidacion();
      await revisar('Escuela');
      await pulsar('Validar');
      const envio = pedido('/v1/evaluaciones/e1/validacion');
      expect(envio.request.body).toEqual({ estado: 'VALIDADA', motivo: null });
      envio.flush({});
      await estable();
      expect(html.querySelector('.aviso.ok')!.textContent).toContain('evaluación validada');
      expect(filas().some((f) => f.includes('Escuela'))).toBe(false);

      await new Promise((r) => setTimeout(r));
      await estable();
      pedido('/v1/consulta-danos/mapa').flush(mapa());
      await estable();
      expect(filas().some((f) => f.includes('Escuela'))).toBe(false);
    });

    it('el rechazo exige motivo y lo envía', async () => {
      await abrirValidacion();
      await revisar('Escuela');
      await pulsar('Rechazar');
      expect(html.querySelector('.detalle .aviso.error')!.textContent).toContain('hay que indicar el motivo');
      servidor.expectNone('/v1/evaluaciones/e1/validacion');

      await escribir('#val-motivo', 'La foto no corresponde');
      await pulsar('Rechazar');
      const envio = pedido('/v1/evaluaciones/e1/validacion');
      expect(envio.request.body).toEqual({ estado: 'RECHAZADA', motivo: 'La foto no corresponde' });
      envio.flush({});
      await estable();
      await new Promise((r) => setTimeout(r));
      await estable();
      pedido('/v1/consulta-danos/mapa').flush(mapa());
      await estable();
    });

    it.each([[403, 'La organización bomberos no participa en el desastre'], [422, 'La evaluación ya está en el estado VALIDADA']])('ante un %i muestra el mensaje del servicio', async (status, mensaje) => {
      await abrirValidacion();
      await revisar('Escuela');
      await pulsar('Validar');
      pedido('/v1/evaluaciones/e1/validacion').flush({ error: mensaje }, { status, statusText: 'Error' });
      await estable();
      expect(html.querySelector('.detalle .aviso.error')!.textContent).toContain(mensaje);
      expect(filas().some((f) => f.includes('Escuela'))).toBe(true);
    });

    it('con un conflicto pendiente no deja decidir y explica por qué', async () => {
      await abrirValidacion();
      await revisar('Puente');
      expect(boton('Validar').disabled).toBe(true);
      expect(boton('Rechazar').disabled).toBe(true);
      expect(texto()).toContain('resuélvalo en la bandeja');
    });

    it('una validada se puede devolver a pendiente', async () => {
      await abrirValidacion();
      const filtro = html.querySelector('#val-estado') as HTMLSelectElement;
      filtro.value = 'TODAS';
      filtro.dispatchEvent(new Event('change'));
      await estable();
      await revisar('Vivienda');
      expect(boton('Validar').disabled).toBe(true);
      await pulsar('Devolver a pendiente');
      const envio = pedido('/v1/evaluaciones/e2/validacion');
      expect(envio.request.body.estado).toBe('PENDIENTE');
      envio.flush({});
      await estable();
      await new Promise((r) => setTimeout(r));
      await estable();
      pedido('/v1/consulta-danos/mapa').flush(mapa());
      await estable();
    });
  });

  describe('bandeja de conflictos', () => {
    async function abrirBandeja(lista: Conflicto[] = [conflicto()]): Promise<void> {
      await abrir(Conflictos);
      const r = pedido('/v1/conflictos');
      expect(r.request.params.get('estado')).toBe('PENDIENTE');
      r.flush(lista);
      await estable();
    }

    const opcion = (valor: string) => html.querySelector(`input[name=decision][value=${valor}]`) as HTMLInputElement;
    async function elegirDecision(valor: string): Promise<void> {
      opcion(valor).click();
      await estable();
    }

    it('muestra las dos versiones completas lado a lado y marca lo distinto', async () => {
      await abrirBandeja();
      await pulsar('Abrir');
      const encabezados = [...html.querySelectorAll('.comparacion thead th')].map((t) => t.textContent);
      expect(encabezados).toEqual(['Campo', 'Versión vigente', 'Versión entrante']);
      const nivel = [...html.querySelectorAll('.comparacion tbody tr')].find((f) => f.textContent?.includes('Nivel de daño'))!;
      expect(nivel.className).toContain('distinta');
      expect(nivel.textContent).toContain('Grave');
      expect(nivel.textContent).toContain('Moderado');
      expect(texto()).toContain('Muro caído');
      expect(texto()).toContain('alcaldia-armenia');
    });

    it('ofrece las cuatro decisiones y habilita las que admite un duplicado', async () => {
      await abrirBandeja();
      await pulsar('Abrir');
      expect(html.querySelectorAll('input[name=decision]').length).toBe(4);
      expect(opcion('PREVALECE_ENTRANTE').disabled).toBe(true);
      expect(opcion('PREVALECE_VIGENTE').disabled).toBe(true);
      expect(opcion('FUSIONAR').disabled).toBe(false);
      expect(opcion('RECHAZAR_DUPLICADO').disabled).toBe(false);
    });

    it('fusionar envía cuál evaluación queda vigente', async () => {
      await abrirBandeja();
      await pulsar('Abrir');
      await elegirDecision('FUSIONAR');
      (html.querySelector('input[name=vigente][value=ENTRANTE]') as HTMLInputElement).click();
      await estable();
      await escribir('#con-justificacion', 'Es la misma escuela');
      await pulsar('Registrar decisión');
      const envio = pedido('/v1/conflictos/c1/resolucion');
      expect(envio.request.body).toEqual({ decision: 'FUSIONAR', justificacion: 'Es la misma escuela', vigente: 'ENTRANTE' });
      envio.flush({});
      await estable();
      expect(html.querySelector('.aviso.ok')!.textContent).toContain('Fusionar');
      expect(texto()).toContain('No hay conflictos pendientes');
    });

    it('un conflicto de versión se resuelve eligiendo una versión, sin enviar vigente', async () => {
      await abrirBandeja([conflicto({ tipo: 'VERSION' })]);
      await pulsar('Abrir');
      expect(opcion('FUSIONAR').disabled).toBe(true);
      await elegirDecision('PREVALECE_VIGENTE');
      await escribir('#con-justificacion', 'La vigente es más reciente');
      await pulsar('Registrar decisión');
      const envio = pedido('/v1/conflictos/c1/resolucion');
      expect(envio.request.body).toEqual({ decision: 'PREVALECE_VIGENTE', justificacion: 'La vigente es más reciente' });
      envio.flush({});
      await estable();
    });

    it('exige decisión y justificación antes de enviar', async () => {
      await abrirBandeja();
      await pulsar('Abrir');
      await pulsar('Registrar decisión');
      expect(texto()).toContain('Elija una decisión');
      await elegirDecision('RECHAZAR_DUPLICADO');
      await pulsar('Registrar decisión');
      expect(texto()).toContain('necesita una justificación');
      servidor.expectNone('/v1/conflictos/c1/resolucion');
    });

    it('ante un 422 muestra el mensaje y conserva el conflicto abierto', async () => {
      await abrirBandeja();
      await pulsar('Abrir');
      await elegirDecision('RECHAZAR_DUPLICADO');
      await escribir('#con-justificacion', 'Son distintos');
      await pulsar('Registrar decisión');
      pedido('/v1/conflictos/c1/resolucion').flush({ error: 'El conflicto c1 ya fue resuelto' }, { status: 422, statusText: 'Regla' });
      await estable();
      expect(html.querySelector('.decision .aviso.error')!.textContent).toContain('ya fue resuelto');
      expect(html.querySelector('.comparacion')).not.toBeNull();
    });

    it('ante un 403 al listar muestra el mensaje del servicio', async () => {
      await abrir(Conflictos);
      pedido('/v1/conflictos').flush({ codigo: 'NO_AUTORIZADO', mensaje: 'Su rol no permite esta operación.' }, { status: 403, statusText: 'Prohibido' });
      await estable();
      expect(html.querySelector('.aviso.error')!.textContent).toContain('Su rol no permite');
    });

    it('un conflicto resuelto muestra la decisión y no el formulario', async () => {
      await abrirBandeja();
      const filtro = html.querySelector('#con-estado') as HTMLSelectElement;
      filtro.value = 'RESUELTO';
      filtro.dispatchEvent(new Event('change'));
      await estable();
      const r = pedido('/v1/conflictos');
      expect(r.request.params.get('estado')).toBe('RESUELTO');
      r.flush([conflicto({ estado: 'RESUELTO', decision: 'FUSIONAR', decididoPor: 'operador.danos', justificacion: 'Misma escuela', instanteResolucion: T })]);
      await estable();
      await pulsar('Abrir');
      expect(html.querySelector('.decision')).toBeNull();
      expect(texto()).toContain('operador.danos');
      expect(texto()).toContain('Misma escuela');
    });

    it('sin versión vigente solo puede prevalecer la entrante', () => {
      const c = conflicto({ tipo: 'VERSION', vigente: null });
      expect(razonNoDisponible(c, 'PREVALECE_ENTRANTE')).toBeNull();
      expect(razonNoDisponible(c, 'PREVALECE_VIGENTE')).toContain('No hay una versión vigente');
      expect(compararVersiones(c)[0].vigente).toBe('Sin versión vigente');
    });
  });

  describe('catálogo de tipos de activo', () => {
    const tipo = { codigo: 'escuela', nombre: 'Escuela', version: 2, criterios: [{ nivel: 'LEVE', criterio: 'Fisuras' }] };

    async function abrirCatalogo(): Promise<void> {
      await abrir(Catalogo);
      pedido('/v1/catalogos/tipos-activo').flush([tipo]);
      await estable();
    }

    async function llenarCriterios(): Promise<void> {
      for (const n of ['SIN_DANO', 'LEVE', 'MODERADO', 'GRAVE', 'DESTRUCCION_TOTAL']) {
        await escribir(`#criterio-${n}`, `Criterio ${n}`);
      }
    }

    it('crea un tipo con un criterio por nivel', async () => {
      await abrirCatalogo();
      await pulsar('Agregar tipo');
      await escribir('#tipo-codigo', 'coliseo');
      await escribir('#tipo-nombre', 'Coliseo');
      await llenarCriterios();
      await pulsar('Guardar');
      const envio = pedido('/v1/catalogos/tipos-activo');
      expect(envio.request.method).toBe('POST');
      expect(envio.request.body.codigo).toBe('coliseo');
      expect(envio.request.body.criterios.length).toBe(5);
      envio.flush({ codigo: 'coliseo', nombre: 'Coliseo', version: 1, criterios: [] });
      await estable();
      expect(filas().length).toBe(2);
      expect(html.querySelector('.aviso.ok')!.textContent).toContain('Coliseo');
    });

    it('edita con PATCH y conserva los criterios cargados', async () => {
      await abrirCatalogo();
      await pulsar('Editar');
      expect((html.querySelector('#criterio-LEVE') as HTMLTextAreaElement).value).toBe('Fisuras');
      expect(html.querySelector('#tipo-codigo')).toBeNull();
      await escribir('#tipo-nombre', 'Institución educativa');
      await pulsar('Guardar');
      const envio = pedido('/v1/catalogos/tipos-activo/escuela');
      expect(envio.request.method).toBe('PATCH');
      expect(envio.request.body.nombre).toBe('Institución educativa');
      envio.flush({ ...tipo, nombre: 'Institución educativa', version: 3 });
      await estable();
      expect(filas()[0]).toContain('Institución educativa');
    });

    it('ante un 422 muestra el mensaje y no cierra el formulario', async () => {
      await abrirCatalogo();
      await pulsar('Agregar tipo');
      await escribir('#tipo-codigo', 'escuela');
      await pulsar('Guardar');
      pedido('/v1/catalogos/tipos-activo').flush({ error: "Ya existe el tipo de activo 'escuela'" }, { status: 422, statusText: 'Regla' });
      await estable();
      expect(texto()).toContain('Ya existe el tipo de activo');
      expect(html.querySelector('#tipo-codigo')).not.toBeNull();
    });
  });

  describe('rutas por rol', () => {
    async function destino(rol: RolRealm): Promise<string> {
      await iniciarSesionComo(oidc, tokenDe('usuario.prueba', [rol]));
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/evaluaciones');
      return router.url;
    }

    it.each<[RolRealm, string]>([['organismo', '/evaluaciones/validacion'], ['autoridad', '/evaluaciones/validacion'], ['operador', '/evaluaciones/conflictos'], ['administrador', '/evaluaciones/catalogo']])('%s entra a su apartado', async (rol, url) => {
      expect(await destino(rol)).toBe(url);
    });

    it('un organismo no entra a la bandeja de conflictos', async () => {
      await iniciarSesionComo(oidc, tokenDe('usuario.prueba', ['organismo']));
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/evaluaciones/conflictos');
      expect(router.url).not.toBe('/evaluaciones/conflictos');
    });
  });
});
