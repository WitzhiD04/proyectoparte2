import { Component, computed, inject } from '@angular/core';
import { RedirectFunction, RouterLink, RouterLinkActive, RouterOutlet, Routes } from '@angular/router';
import { conRoles } from '../../nucleo/autenticacion/guardas';
import { tieneAlgunRol } from '../../nucleo/menu';
import { RolRealm, Sesion } from '../../nucleo/sesion';

const APARTADOS: readonly { ruta: string; etiqueta: string; roles: readonly RolRealm[] }[] = [
  { ruta: 'validacion', etiqueta: 'Validación', roles: ['autoridad', 'organismo'] },
  { ruta: 'conflictos', etiqueta: 'Bandeja de conflictos', roles: ['operador'] },
  { ruta: 'catalogo', etiqueta: 'Tipos de activo', roles: ['administrador'] },
];

const apartadosDe = (roles: readonly RolRealm[]) => APARTADOS.filter((a) => tieneAlgunRol(roles, a.roles));
const rolesDe = (ruta: string) => APARTADOS.find((a) => a.ruta === ruta)!.roles;

/** Lleva a cada usuario al primer apartado que permiten los roles de su token. */
const alPrimerApartado: RedirectFunction = () => {
  const primero = apartadosDe(inject(Sesion).roles())[0];
  return primero ? `/evaluaciones/${primero.ruta}` : '/';
};

@Component({
  selector: 'app-evaluaciones',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    @if (apartados().length > 1) {
      <nav class="apartados" aria-label="Apartados de evaluaciones">
        @for (a of apartados(); track a.ruta) { <a [routerLink]="a.ruta" routerLinkActive="activo">{{ a.etiqueta }}</a> }
      </nav>
    }
    <router-outlet />
  `,
  styles: `
    .apartados { display: flex; flex-wrap: wrap; gap: 0.5rem 1.25rem; margin-bottom: 1.25rem; }
    .apartados a { font-weight: 600; text-decoration: none; border-bottom: 3px solid transparent; padding-bottom: 0.15rem; }
    .apartados a.activo { border-bottom-color: var(--senal); }
  `,
})
export class ModuloEvaluaciones {
  private readonly sesion = inject(Sesion);
  readonly apartados = computed(() => apartadosDe(this.sesion.roles()));
}

export const RUTAS_EVALUACIONES: Routes = [
  {
    path: '',
    component: ModuloEvaluaciones,
    children: [
      { path: '', pathMatch: 'full', redirectTo: alPrimerApartado },
      { path: 'validacion', canMatch: [conRoles(rolesDe('validacion'))], loadComponent: () => import('./validacion').then((m) => m.Validacion) },
      { path: 'conflictos', canMatch: [conRoles(rolesDe('conflictos'))], loadComponent: () => import('./conflictos').then((m) => m.Conflictos) },
      { path: 'catalogo', canMatch: [conRoles(rolesDe('catalogo'))], loadComponent: () => import('./catalogo').then((m) => m.Catalogo) },
    ],
  },
];
