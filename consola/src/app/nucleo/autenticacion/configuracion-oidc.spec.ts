import { EMISOR_OIDC, configuracionOidc } from './configuracion-oidc';

describe('configuración OIDC', () => {
  const configuracion = configuracionOidc('http://localhost:8080');

  it('usa el flujo de código del cliente consola contra el emisor del contrato', () => {
    expect(configuracion).toMatchObject({ authority: EMISOR_OIDC, clientId: 'consola', responseType: 'code' });
    expect(EMISOR_OIDC).toBe('http://localhost:8180/realms/plataforma');
  });

  it('vuelve a la pantalla de ingreso, y es la consola la que decide a dónde seguir', () => {
    expect(configuracion.redirectUrl).toBe('http://localhost:8080/ingresar');
    expect(configuracion.postLogoutRedirectUri).toBe('http://localhost:8080/ingresar');
    expect(configuracion.triggerAuthorizationResultEvent).toBe(true);
  });

  it('renueva con refresh token antes de que venza el token', () => {
    expect(configuracion).toMatchObject({ silentRenew: true, useRefreshToken: true });
  });
});
