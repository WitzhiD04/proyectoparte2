import { LogLevel, OpenIdConfiguration } from 'angular-auth-oidc-client';

/** Emisor del contrato de identidad: el mismo para todos los clientes (docs/encargos/convenciones.md). */
export const EMISOR_OIDC = 'http://localhost:8180/realms/plataforma';

/** Ruta pública de la consola: recibe la respuesta de Keycloak y muestra el inicio de sesión. */
export const RUTA_INGRESO = '/ingresar';

/**
 * Inicio de sesión con el flujo de código y PKCE S256 (DA-13, DT-12). La librería guarda los tokens en sessionStorage
 * y renueva el token con el refresh token antes de que venza. El Bearer lo agrega nuestro interceptor.
 */
export function configuracionOidc(origen: string): OpenIdConfiguration {
  return {
    authority: EMISOR_OIDC,
    clientId: 'consola',
    responseType: 'code',
    scope: 'openid profile',
    redirectUrl: origen + RUTA_INGRESO,
    postLogoutRedirectUri: origen + RUTA_INGRESO,
    // La librería no navega al volver de Keycloak: la guarda de /ingresar lleva al destino que pidió el usuario.
    triggerAuthorizationResultEvent: true,
    silentRenew: true,
    useRefreshToken: true,
    // Keycloak entrega el refresh token sin offline_access; la consola no necesita sesiones fuera de línea.
    disableRefreshTokenOfflineAccessScopeWarning: true,
    renewTimeBeforeTokenExpiresInSeconds: 60,
    autoUserInfo: false,
    logLevel: LogLevel.Warn,
  };
}
