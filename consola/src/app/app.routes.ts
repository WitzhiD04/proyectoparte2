import { Routes } from '@angular/router';
import { conRoles, conSesion, redirigirAlInicio, sinSesion } from './nucleo/autenticacion/guardas';
import { ROLES_EVALUACIONES, ROLES_MAPA, ROLES_RECONSTRUCCION } from './nucleo/menu';

/** Cada área es un módulo de carga diferida (DA-13). */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: redirigirAlInicio },
  { path: 'ingresar', canActivate: [sinSesion], loadComponent: () => import('./modulos/acceso/ingresar').then((m) => m.Ingresar) },
  { path: 'sin-acceso', canMatch: [conSesion], loadComponent: () => import('./modulos/acceso/sin-acceso').then((m) => m.SinAcceso) },
  {
    path: 'reconstruccion',
    canMatch: [conRoles(ROLES_RECONSTRUCCION)],
    loadChildren: () => import('./modulos/reconstruccion/reconstruccion.routes').then((m) => m.RUTAS_RECONSTRUCCION),
  },
  { path: 'mapa', canMatch: [conRoles(ROLES_MAPA)], loadChildren: () => import('./modulos/mapa/mapa.routes').then((m) => m.RUTAS_MAPA) },
  {
    path: 'evaluaciones',
    canMatch: [conRoles(ROLES_EVALUACIONES)],
    loadChildren: () => import('./modulos/evaluaciones/evaluaciones.routes').then((m) => m.RUTAS_EVALUACIONES),
  },
  { path: '**', redirectTo: '' },
];
