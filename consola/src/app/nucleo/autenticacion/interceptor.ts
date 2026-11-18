import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { Sesion } from '../sesion';

const PREFIJO_API = '/v1';

function conToken(peticion: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return peticion.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/**
 * Agrega el token a las peticiones de la API (el gateway y cada servicio lo validan, DA-15). Lo renueva si está por
 * vencer y, si aun así llega un 401, renueva una vez y repite la petición. Si no se puede, la sesión terminó.
 * Las peticiones a Keycloak no pasan por aquí: solo las de /v1 llevan el token.
 */
export const tokenInterceptor: HttpInterceptorFn = (peticion: HttpRequest<unknown>, siguiente: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  if (!peticion.url.startsWith(PREFIJO_API)) {
    return siguiente(peticion);
  }
  const sesion = inject(Sesion);
  const terminarSesion = (error: unknown) => {
    sesion.sesionVencida();
    return throwError(() => error);
  };

  return sesion.tokenVigente().pipe(
    catchError(terminarSesion),
    switchMap((token) => siguiente(conToken(peticion, token)).pipe(
      catchError((error: unknown) => {
        if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
          return throwError(() => error);
        }
        return sesion.renovar().pipe(
          catchError(() => terminarSesion(error)),
          switchMap((nuevo) => siguiente(conToken(peticion, nuevo)).pipe(
            catchError((repetido: unknown) => repetido instanceof HttpErrorResponse && repetido.status === 401
              ? terminarSesion(repetido)
              : throwError(() => repetido)),
          )),
        );
      }),
    )),
  );
};
