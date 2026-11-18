import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideAuth } from 'angular-auth-oidc-client';
import { routes } from './app.routes';
import { configuracionOidc } from './nucleo/autenticacion/configuracion-oidc';
import { tokenInterceptor } from './nucleo/autenticacion/interceptor';
import { Sesion } from './nucleo/sesion';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([tokenInterceptor])),
    provideAuth({ config: configuracionOidc(window.location.origin) }),
    // La sesión se resuelve antes de la primera navegación: las guardas ya saben quién es el usuario.
    provideAppInitializer(() => inject(Sesion).iniciar()),
  ],
};
