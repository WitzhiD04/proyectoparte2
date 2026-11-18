import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { RolRealm } from './nucleo/sesion';
import { OidcFalso, iniciarSesionComo, proveedoresOidc, tokenDe } from './pruebas/apoyo';

describe('App', () => {
  let oidc: OidcFalso;
  let servidor: HttpTestingController;

  beforeEach(() => {
    oidc = new OidcFalso();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting(), ...proveedoresOidc(oidc)],
    });
    servidor = TestBed.inject(HttpTestingController);
  });

  async function abrir(url: string, claims?: Record<string, unknown>) {
    if (claims) {
      await iniciarSesionComo(oidc, claims);
    }
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl(url);
    await fixture.whenStable();
    return { fixture, html: fixture.nativeElement as HTMLElement };
  }

  const enlaces = (html: HTMLElement) => [...html.querySelectorAll('nav a')].map((a) => a.textContent?.trim());
  const comoRol = (rol: RolRealm, extra = {}) => tokenDe('usuario.prueba', [rol], { name: 'Usuario de prueba', ...extra });

  it('sin sesión no muestra menú ni datos de usuario', async () => {
    const { html } = await abrir('/ingresar');
    expect(html.querySelector('nav')).toBeNull();
    expect(html.querySelector('.salir')).toBeNull();
    expect(html.querySelector('.modulo')?.textContent).toContain('Consola de coordinación');
  });

  it('arma el menú con los roles del token y ya no tiene selector de perfil', async () => {
    const { html } = await abrir('/reconstruccion/proyectos', comoRol('auditor'));
    expect(enlaces(html)).toEqual(['Proyectos', 'Consolidado del programa', 'Mapa']);
    expect(html.querySelector('#perfil')).toBeNull();
    expect(html.textContent).not.toContain('Perfil de prueba');
    servidor.expectOne('/v1/reconstruccion/desastres').flush([]);
  });

  it('muestra quién inició sesión, con su ámbito', async () => {
    const { html } = await abrir('/reconstruccion/proyectos', comoRol('autoridad', { ambito: 'REGIONAL', territorio: '63' }));
    expect(html.querySelector('.nombre')?.textContent).toBe('Usuario de prueba');
    expect(html.querySelector('.rol')?.textContent).toBe('Autoridad regional');
    servidor.expectOne('/v1/reconstruccion/desastres').flush([]);
  });

  it('en Reconstrucción carga los desastres y elige el que está en reconstrucción', async () => {
    const { fixture, html } = await abrir('/reconstruccion/proyectos', comoRol('autoridad'));
    servidor.expectOne('/v1/reconstruccion/desastres').flush([
      { id: 'd1', nombre: 'Cerrado', estado: 'CERRADO', enReconstruccion: false },
      { id: 'd2', nombre: 'Sismo', estado: 'EN_RECONSTRUCCION', enReconstruccion: true },
    ]);
    await fixture.whenStable();
    expect(html.querySelector('.modulo')?.textContent).toContain('Reconstrucción');
    expect(html.querySelectorAll('#desastre option').length).toBe(2);
    servidor.expectOne((r) => r.url === '/v1/proyectos' && r.params.get('desastre') === 'd2').flush([]);
  });

  it('fuera de Reconstrucción no pide desastres ni muestra el selector', async () => {
    const { html } = await abrir('/mapa', comoRol('organismo'));
    expect(html.querySelector('.modulo')?.textContent).toContain('Mapa');
    expect(html.querySelector('#desastre')).toBeNull();
    expect(html.textContent).toContain('todavía no tiene contenido');
    servidor.expectNone('/v1/reconstruccion/desastres');
  });

  it('si no puede leer los desastres muestra el mensaje del servicio', async () => {
    const { fixture, html } = await abrir('/reconstruccion/proyectos', comoRol('auditor'));
    servidor.expectOne('/v1/reconstruccion/desastres').flush({ codigo: 'NO_AUTORIZADO', mensaje: 'Su rol no permite esta operación.' }, { status: 403, statusText: 'Prohibido' });
    await fixture.whenStable();
    expect(html.querySelector('[role=alert]')?.textContent).toContain('Su rol no permite esta operación.');
  });

  it('el botón cierra la sesión', async () => {
    const { html } = await abrir('/mapa', comoRol('operador'));
    (html.querySelector('.salir') as HTMLButtonElement).click();
    expect(oidc.logoff).toHaveBeenCalledOnce();
  });
});
