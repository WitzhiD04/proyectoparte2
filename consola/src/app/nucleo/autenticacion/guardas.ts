import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, RedirectFunction, Router, UrlTree } from '@angular/router';
import { inicioPara, tieneAlgunRol } from '../menu';
import { RolRealm, Sesion, destinoSeguro } from '../sesion';
import { RUTA_INGRESO } from './configuracion-oidc';

function alIngreso(router: Router, volver: string): UrlTree {
  return router.createUrlTree([RUTA_INGRESO], { queryParams: { volver } });
}

/**
 * Exige sesión y uno de los roles. Es de tipo canMatch para que el código de un módulo no se descargue si el usuario
 * no puede entrar. Es comodidad de la interfaz: quien autoriza de verdad es el gateway y cada servicio.
 */
export function conRoles(admitidos: readonly RolRealm[]): CanMatchFn {
  return (_ruta, segmentos) => {
    const sesion = inject(Sesion);
    const router = inject(Router);
    if (!sesion.autenticada()) {
      return alIngreso(router, '/' + segmentos.map((s) => s.path).join('/'));
    }
    return tieneAlgunRol(sesion.roles(), admitidos) ? true : router.parseUrl('/');
  };
}

/** Exige sesión, sin importar el rol. */
export const conSesion: CanMatchFn = (_ruta, segmentos) => {
  const sesion = inject(Sesion);
  return sesion.autenticada() ? true : alIngreso(inject(Router), '/' + segmentos.map((s) => s.path).join('/'));
};

/** Quien ya inició sesión no ve la pantalla de ingreso: sigue a donde iba. */
export const sinSesion: CanActivateFn = (ruta) => {
  const sesion = inject(Sesion);
  if (!sesion.autenticada()) {
    return true;
  }
  return inject(Router).parseUrl(sesion.tomarDestino() ?? destinoSeguro(ruta.queryParamMap.get('volver')) ?? '/');
};

/** La raíz lleva a la primera pantalla que permiten los roles del token. */
export const redirigirAlInicio: RedirectFunction = () => {
  const sesion = inject(Sesion);
  if (!sesion.autenticada()) {
    return RUTA_INGRESO;
  }
  return inicioPara(sesion.roles()) ?? '/sin-acceso';
};
