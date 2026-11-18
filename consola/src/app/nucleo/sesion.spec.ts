import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EventTypes } from 'angular-auth-oidc-client';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { EventosFalsos, OidcFalso, iniciarSesionComo, proveedoresOidc, tokenDe } from '../pruebas/apoyo';
import { leerError } from './api';
import { Sesion, destinoSeguro, identidadDesdeToken } from './sesion';

describe('identidad del token', () => {
  it('lee usuario, roles del realm, ámbito y territorio, e ignora los roles de Keycloak', () => {
    const identidad = identidadDesdeToken(tokenDe('alcaldia.armenia', ['autoridad'], {
      name: 'Alcaldía Armenia', organizacion: 'alcaldia-armenia', ambito: 'local', territorio: '63001',
    }));
    expect(identidad).toMatchObject({
      usuario: 'alcaldia.armenia', nombre: 'Alcaldía Armenia', roles: ['autoridad'], organizacion: 'alcaldia-armenia',
      ambito: 'LOCAL', territorio: '63001',
    });
  });

  it('sin usuario no hay identidad', () => {
    expect(identidadDesdeToken({ realm_access: { roles: ['autoridad'] } })).toBeNull();
    expect(identidadDesdeToken(null)).toBeNull();
  });
});

describe('destino después de iniciar sesión', () => {
  it('acepta rutas internas', () => expect(destinoSeguro('/reconstruccion/proyectos?a=1')).toBe('/reconstruccion/proyectos?a=1'));

  it.each(['https://otro.example', '//otro.example', '/\\otro.example', '/ingresar?code=1', '', null])('rechaza %s', (destino) => {
    expect(destinoSeguro(destino)).toBeNull();
  });
});

describe('Sesion', () => {
  let oidc: OidcFalso;
  let eventos: EventosFalsos;

  beforeEach(() => {
    sessionStorage.clear();
    oidc = new OidcFalso();
    eventos = new EventosFalsos();
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: 'ingresar', children: [] }]), ...proveedoresOidc(oidc, eventos)] });
  });

  it('con varios roles aplica la misma prioridad que Reconstrucción', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('varios', ['auditor', 'autoridad', 'responsable-proyecto']));
    expect(sesion.rolReconstruccion()).toBe('Autoridad');
    expect(sesion.esResponsable()).toBe(false);
  });

  it('sin sesión no hay identidad ni aviso', async () => {
    const sesion = TestBed.inject(Sesion);
    await sesion.iniciar();
    expect(sesion.autenticada()).toBe(false);
    expect(sesion.aviso()).toBeNull();
  });

  it('si Keycloak no responde al arrancar, la consola arranca y lo avisa', async () => {
    oidc.resultadoInicio = throwError(() => new Error('sin red'));
    const sesion = TestBed.inject(Sesion);
    await sesion.iniciar();
    expect(sesion.autenticada()).toBe(false);
    expect(sesion.aviso()).toContain('identidad no responde');
  });

  it('ingresar recuerda el destino una sola vez y va a Keycloak', () => {
    const sesion = TestBed.inject(Sesion);
    sesion.ingresar('/reconstruccion/consolidado');
    expect(oidc.authorize).toHaveBeenCalledOnce();
    expect(sesion.tomarDestino()).toBe('/reconstruccion/consolidado');
    expect(sesion.tomarDestino()).toBeNull();
  });

  it('si Keycloak no responde al ingresar, avisa y no redirige', () => {
    oidc.preloadAuthWellKnownDocument.mockReturnValue(throwError(() => new Error('sin red')));
    const sesion = TestBed.inject(Sesion);
    sesion.ingresar();
    expect(oidc.authorize).not.toHaveBeenCalled();
    expect(sesion.aviso()).toContain('identidad no responde');
  });

  it('usa el token vigente sin renovarlo', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('auditor.1', ['auditor']));
    expect(await firstValueFrom(sesion.tokenVigente())).toBe('token-1');
    expect(oidc.forceRefreshSession).not.toHaveBeenCalled();
  });

  it('renueva antes de usar un token que está por vencer', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('auditor.1', ['auditor'], { exp: Math.floor(Date.now() / 1000) + 5 }));
    expect(await firstValueFrom(sesion.tokenVigente())).toBe('token-2');
  });

  it('las renovaciones simultáneas son una sola', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('auditor.1', ['auditor']));
    const respuesta = new Subject<unknown>();
    oidc.forceRefreshSession.mockReturnValue(respuesta);
    const tokens = Promise.all([firstValueFrom(sesion.renovar()), firstValueFrom(sesion.renovar())]);
    respuesta.next({ isAuthenticated: true, accessToken: 'token-3' });
    respuesta.complete();
    expect(await tokens).toEqual(['token-3', 'token-3']);
    expect(oidc.forceRefreshSession).toHaveBeenCalledOnce();
  });

  it('actualiza la identidad cuando la librería renueva el token por su cuenta', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('auditor.1', ['auditor']));
    oidc.claims = tokenDe('auditor.1', ['auditor', 'operador']);
    eventos.eventos.next({ type: EventTypes.NewAuthenticationResult, value: null });
    expect(sesion.roles()).toEqual(['auditor', 'operador']);
  });

  it('una sesión vencida se limpia y avisa', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('auditor.1', ['auditor']));
    sesion.sesionVencida();
    expect(oidc.logoffLocal).toHaveBeenCalledOnce();
    expect(sesion.autenticada()).toBe(false);
    expect(sesion.aviso()).toContain('venció');
  });

  it('cerrar sesión pasa por Keycloak', async () => {
    const sesion = await iniciarSesionComo(oidc, tokenDe('auditor.1', ['auditor']));
    oidc.logoff.mockReturnValue(of(null));
    sesion.salir();
    expect(oidc.logoff).toHaveBeenCalledOnce();
  });
});

describe('leerError', () => {
  const error = (status: number, cuerpo: unknown) => new HttpErrorResponse({ status, error: cuerpo });

  it('muestra el mensaje del servicio ante un 403', () => {
    expect(leerError(error(403, { codigo: 'NO_AUTORIZADO', mensaje: 'Este proyecto lo aprueba la autoridad LOCAL del municipio 63001.' })).mensaje)
      .toBe('Este proyecto lo aprueba la autoridad LOCAL del municipio 63001.');
  });

  it('entiende el formato de error de Evaluación de Daños', () => {
    expect(leerError(error(403, { error: 'No participa en el desastre' })).mensaje).toBe('No participa en el desastre');
  });

  it('un 403 sin cuerpo no se confunde con una falla de conexión', () => {
    expect(leerError(error(403, null)).codigo).toBe('NO_AUTORIZADO');
    expect(leerError(error(0, null)).codigo).toBe('SIN_CONEXION');
  });
});
