import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Proyecto } from '../../../nucleo/modelos';
import { RolRealm, Sesion } from '../../../nucleo/sesion';
import { OidcFalso, iniciarSesionComo, proveedoresOidc, tokenDe } from '../../../pruebas/apoyo';
import { DetalleProyecto } from './proyecto';
import { Proyectos } from './proyectos';

const proyecto = (cambios: Partial<Proyecto> = {}): Proyecto => ({
  id: 'p1', idDesastre: 'd1', nombre: 'Reconstrucción de Escuela', alcance: 'Municipal', estado: 'Formulado',
  presupuesto: 100, fuenteFinanciacion: 'Fondo', responsable: 'ing.perez', contratista: null, inicio: '2027-01-01', fin: '2027-06-01',
  avance: 0, costoEjecutado: 0, enSobrecosto: false, version: 3, ...cambios,
});

describe('pantallas de Reconstrucción con la sesión del token', () => {
  let oidc: OidcFalso;
  let servidor: HttpTestingController;

  beforeEach(() => {
    oidc = new OidcFalso();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), ...proveedoresOidc(oidc)],
    });
    servidor = TestBed.inject(HttpTestingController);
  });

  async function detalle(usuario: string, rol: RolRealm, datos: Proyecto) {
    await iniciarSesionComo(oidc, tokenDe(usuario, [rol]));
    const fixture = TestBed.createComponent(DetalleProyecto);
    fixture.componentRef.setInput('id', datos.id);
    fixture.detectChanges();
    servidor.expectOne(`/v1/proyectos/${datos.id}`).flush(datos);
    if (rol !== 'responsable-proyecto') {
      servidor.expectOne(`/v1/proyectos/${datos.id}/historial`).flush({ idProyecto: datos.id, fecha: null, estadoEnFecha: null, entradas: [] });
    }
    await fixture.whenStable();
    return { fixture, html: fixture.nativeElement as HTMLElement };
  }

  const boton = (html: HTMLElement, texto: string) =>
    [...html.querySelectorAll('button')].find((b) => b.textContent?.includes(texto)) as HTMLButtonElement | undefined;

  it('ante un 403 al aprobar muestra el mensaje del servicio', async () => {
    const { fixture, html } = await detalle('gobernacion.quindio', 'autoridad', proyecto());
    boton(html, 'Aprobar proyecto')!.click();
    servidor.expectOne('/v1/proyectos/p1/transiciones').flush(
      { codigo: 'NO_AUTORIZADO', mensaje: 'Este proyecto lo aprueba la autoridad LOCAL del municipio 63001.' },
      { status: 403, statusText: 'Prohibido' },
    );
    await fixture.whenStable();
    expect(html.querySelector('[role=alert]')?.textContent).toContain('Este proyecto lo aprueba la autoridad LOCAL del municipio 63001.');
  });

  it.each([
    ['Municipal', 'local del municipio'],
    ['Regional', 'regional del departamento'],
    ['Nacional', 'nacional'],
  ])('un proyecto %s dice qué autoridad lo aprueba', async (alcance, aprobador) => {
    const { html } = await detalle('auditor.1', 'auditor', proyecto({ alcance }));
    expect(html.textContent).toContain(`Lo aprueba la autoridad ${aprobador}.`);
  });

  it('el responsable asignado se reconoce por el usuario del token', async () => {
    const { html } = await detalle('ing.perez', 'responsable-proyecto', proyecto({ estado: 'Aprobado' }));
    expect(boton(html, 'Iniciar ejecución')).toBeDefined();
  });

  it('otro responsable no ve las acciones del proyecto', async () => {
    const { html } = await detalle('ing.gomez', 'responsable-proyecto', proyecto({ estado: 'Aprobado' }));
    expect(boton(html, 'Iniciar ejecución')).toBeUndefined();
    expect(html.textContent).toContain('Este proyecto tiene otro responsable.');
  });

  it('el auditor solo consulta, y la cabecera queda en el desastre del proyecto', async () => {
    const { html } = await detalle('auditor.1', 'auditor', proyecto());
    expect(TestBed.inject(Sesion).idDesastre()).toBe('d1');
    expect(boton(html, 'Aprobar proyecto')).toBeUndefined();
    expect(html.textContent).toContain('Historial');
  });

  it('la lista del responsable trae solo sus proyectos, con enlaces al módulo', async () => {
    const sesion: Sesion = await iniciarSesionComo(oidc, tokenDe('ing.perez', ['responsable-proyecto']));
    sesion.idDesastre.set('d1');
    const fixture = TestBed.createComponent(Proyectos);
    fixture.detectChanges();
    servidor.expectOne((r) => r.url === '/v1/proyectos').flush([proyecto(), proyecto({ id: 'p2', nombre: 'Otro', responsable: 'ing.gomez' })]);
    await fixture.whenStable();
    const enlaces = [...(fixture.nativeElement as HTMLElement).querySelectorAll('td a')];
    expect(enlaces.map((a) => a.getAttribute('href'))).toEqual(['/reconstruccion/proyectos/p1']);
  });
});
