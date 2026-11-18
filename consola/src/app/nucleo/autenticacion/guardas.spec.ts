import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { FABRICA_LIENZO } from '../../modulos/mapa/lienzo-mapa';
import { LienzoFalso, OidcFalso, iniciarSesionComo, proveedoresOidc, tokenDe } from '../../pruebas/apoyo';
import { RolRealm } from '../sesion';

describe('rutas protegidas', () => {
  let oidc: OidcFalso;

  beforeEach(() => {
    sessionStorage.clear();
    oidc = new OidcFalso();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        ...proveedoresOidc(oidc),
        { provide: FABRICA_LIENZO, useValue: () => new LienzoFalso() },
      ],
    });
  });

  async function ir(url: string, roles?: RolRealm[]): Promise<string> {
    if (roles) {
      await iniciarSesionComo(oidc, tokenDe('usuario', roles));
    }
    const arnes = await RouterTestingHarness.create();
    await arnes.navigateByUrl(url);
    return TestBed.inject(Router).url;
  }

  it('sin sesión lleva al ingreso y recuerda a dónde iba', async () => {
    expect(await ir('/reconstruccion/consolidado')).toBe('/ingresar?volver=%2Freconstruccion%2Fconsolidado');
  });

  it('sin sesión, la raíz lleva al ingreso', async () => {
    expect(await ir('/')).toBe('/ingresar');
  });

  it('la raíz lleva a la primera pantalla del rol', async () => {
    expect(await ir('/', ['auditor'])).toBe('/reconstruccion/proyectos');
  });

  it('un organismo no entra a Reconstrucción', async () => {
    expect(await ir('/reconstruccion/proyectos', ['organismo'])).toBe('/mapa');
  });

  it('el responsable no entra a candidatas ni al consolidado', async () => {
    expect(await ir('/reconstruccion/candidatas', ['responsable-proyecto'])).toBe('/reconstruccion/proyectos');
  });

  it('un evaluador sin secciones ve el aviso', async () => {
    expect(await ir('/', ['evaluador'])).toBe('/sin-acceso');
  });

  it('evaluaciones lleva a cada rol a su apartado', async () => {
    expect(await ir('/evaluaciones', ['operador'])).toBe('/evaluaciones/conflictos');
  });

  it('con sesión, el ingreso sigue al destino y no acepta uno externo', async () => {
    expect(await ir('/ingresar?volver=https:%2F%2Fotro.example', ['autoridad'])).toBe('/reconstruccion/proyectos');
  });
});
