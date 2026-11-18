import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentInjector, effect, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { OidcFalso, iniciarSesionComo, proveedoresOidc, tokenDe } from '../../pruebas/apoyo';
import { Sesion } from '../sesion';
import { tokenInterceptor } from './interceptor';

describe('tokenInterceptor', () => {
  let oidc: OidcFalso;
  let http: HttpClient;
  let servidor: HttpTestingController;
  let sesion: Sesion;

  beforeEach(async () => {
    oidc = new OidcFalso();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'ingresar', children: [] }]),
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        ...proveedoresOidc(oidc),
      ],
    });
    http = TestBed.inject(HttpClient);
    servidor = TestBed.inject(HttpTestingController);
    sesion = await iniciarSesionComo(oidc, tokenDe('alcaldia.armenia', ['autoridad']));
  });

  afterEach(() => servidor.verify());

  it('agrega el Bearer a la API y ningún encabezado de identidad', () => {
    http.get('/v1/proyectos').subscribe();
    const peticion = servidor.expectOne('/v1/proyectos').request;
    expect(peticion.headers.get('Authorization')).toBe('Bearer token-1');
    expect(peticion.headers.has('X-Usuario')).toBe(false);
    expect(peticion.headers.has('X-Rol')).toBe(false);
    expect(peticion.headers.has('X-Roles')).toBe(false);
  });

  it('no envía el token fuera de la API', () => {
    http.get('http://localhost:8180/realms/plataforma/x').subscribe();
    expect(servidor.expectOne('http://localhost:8180/realms/plataforma/x').request.headers.has('Authorization')).toBe(false);
  });

  it('ante un 401 renueva una vez y repite la petición', () => {
    let respuesta: unknown;
    http.get('/v1/proyectos').subscribe((r) => (respuesta = r));
    servidor.expectOne('/v1/proyectos').flush({}, { status: 401, statusText: 'No autorizado' });
    const repetida = servidor.expectOne('/v1/proyectos');
    expect(repetida.request.headers.get('Authorization')).toBe('Bearer token-2');
    repetida.flush([{ id: 1 }]);
    expect(respuesta).toEqual([{ id: 1 }]);
    expect(oidc.forceRefreshSession).toHaveBeenCalledOnce();
  });

  it('repite la petición una sola vez aunque la renovación emita más de un resultado', () => {
    const renovado = { isAuthenticated: true, accessToken: 'token-2' };
    oidc.forceRefreshSession.mockReturnValue(of(renovado, renovado));
    http.post('/v1/proyectos/1/transiciones', {}).subscribe();
    servidor.expectOne('/v1/proyectos/1/transiciones').flush({}, { status: 401, statusText: 'No autorizado' });
    servidor.expectOne('/v1/proyectos/1/transiciones').flush({});
  });

  it('una petición hecha dentro de un effect no se repite cuando cambia la identidad', () => {
    runInInjectionContext(TestBed.inject(EnvironmentInjector), () => {
      effect(() => { http.get('/v1/proyectos').subscribe(); });
    });
    TestBed.tick();
    servidor.expectOne('/v1/proyectos').flush([]);
    sesion.identidad.update((actual) => ({ ...actual!, vence: actual!.vence + 900 }));
    TestBed.tick();
    servidor.expectNone('/v1/proyectos');
  });

  it('si la renovación falla, termina la sesión', () => {
    oidc.forceRefreshSession.mockReturnValue(throwError(() => new Error('refresh vencido')));
    let error: HttpErrorResponse | undefined;
    http.get('/v1/proyectos').subscribe({ error: (e) => (error = e) });
    servidor.expectOne('/v1/proyectos').flush({}, { status: 401, statusText: 'No autorizado' });
    expect(error?.status).toBe(401);
    expect(sesion.autenticada()).toBe(false);
    expect(sesion.aviso()).toContain('venció');
  });

  it('si el token nuevo también recibe 401, termina la sesión sin insistir', () => {
    http.get('/v1/proyectos').subscribe({ error: () => undefined });
    servidor.expectOne('/v1/proyectos').flush({}, { status: 401, statusText: 'No autorizado' });
    servidor.expectOne('/v1/proyectos').flush({}, { status: 401, statusText: 'No autorizado' });
    expect(sesion.autenticada()).toBe(false);
  });

  it('un 403 llega a la pantalla: no renueva ni cierra la sesión', () => {
    let error: HttpErrorResponse | undefined;
    http.post('/v1/proyectos/1/transiciones', {}).subscribe({ error: (e) => (error = e) });
    servidor.expectOne('/v1/proyectos/1/transiciones').flush({ codigo: 'NO_AUTORIZADO', mensaje: 'Regla del servicio' }, { status: 403, statusText: 'Prohibido' });
    expect(error?.error.mensaje).toBe('Regla del servicio');
    expect(oidc.forceRefreshSession).not.toHaveBeenCalled();
    expect(sesion.autenticada()).toBe(true);
  });
});
