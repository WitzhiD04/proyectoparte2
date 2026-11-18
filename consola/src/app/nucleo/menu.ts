import { RolRealm } from './sesion';

export interface EntradaMenu {
  etiqueta: string;
  ruta: string;
  modulo: string;
  roles: readonly RolRealm[];
  /** Pantalla que no es de Angular: se abre con un enlace normal. */
  externa?: boolean;
}

export const ROLES_RECONSTRUCCION: readonly RolRealm[] = ['autoridad', 'responsable-proyecto', 'auditor'];
export const ROLES_CANDIDATAS: readonly RolRealm[] = ['autoridad'];
export const ROLES_CONSOLIDADO: readonly RolRealm[] = ['autoridad', 'auditor'];
export const ROLES_MAPA: readonly RolRealm[] = ['autoridad', 'responsable-proyecto', 'auditor', 'organismo', 'operador', 'administrador'];
export const ROLES_EVALUACIONES: readonly RolRealm[] = ['autoridad', 'organismo', 'operador', 'administrador'];
export const ROLES_TABLERO: readonly RolRealm[] = ['autoridad', 'organismo', 'operador', 'auditor'];

/**
 * Menú de la consola. Cada entrada declara los roles del token que la ven, los mismos que exige su ruta en el gateway.
 * El rol "evaluador" trabaja en la app móvil y no tiene entradas aquí.
 */
export const MENU: readonly EntradaMenu[] = [
  { etiqueta: 'Evaluaciones candidatas', ruta: '/reconstruccion/candidatas', modulo: 'Reconstrucción', roles: ROLES_CANDIDATAS },
  { etiqueta: 'Proyectos', ruta: '/reconstruccion/proyectos', modulo: 'Reconstrucción', roles: ROLES_RECONSTRUCCION },
  { etiqueta: 'Consolidado del programa', ruta: '/reconstruccion/consolidado', modulo: 'Reconstrucción', roles: ROLES_CONSOLIDADO },
  { etiqueta: 'Mapa', ruta: '/mapa', modulo: 'Mapa', roles: ROLES_MAPA },
  { etiqueta: 'Evaluaciones', ruta: '/evaluaciones', modulo: 'Evaluaciones', roles: ROLES_EVALUACIONES },
  { etiqueta: 'Tablero de daños', ruta: '/tablero-danos', modulo: 'Tablero de daños', roles: ROLES_TABLERO, externa: true },
];

export function tieneAlgunRol(roles: readonly RolRealm[], admitidos: readonly RolRealm[]): boolean {
  return roles.some((rol) => admitidos.includes(rol));
}

export function menuPara(roles: readonly RolRealm[]): EntradaMenu[] {
  return MENU.filter((entrada) => tieneAlgunRol(roles, entrada.roles));
}

/** La pantalla de inicio de cada usuario. Proyectos va primero para quien opera en Reconstrucción. */
export function inicioPara(roles: readonly RolRealm[]): string | null {
  const entradas = menuPara(roles).filter((e) => !e.externa);
  return (entradas.find((e) => e.ruta === '/reconstruccion/proyectos') ?? entradas[0])?.ruta ?? null;
}

/** Nombre del módulo de una URL, para el título de la cabecera. */
export function moduloDe(url: string): string | null {
  const ruta = url.split(/[?#]/)[0];
  return MENU.find((e) => ruta === e.ruta || ruta.startsWith(`/${e.ruta.split('/')[1]}/`) || ruta === `/${e.ruta.split('/')[1]}`)?.modulo ?? null;
}
