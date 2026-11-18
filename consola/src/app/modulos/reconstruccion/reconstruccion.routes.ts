import { Routes } from '@angular/router';
import { conRoles } from '../../nucleo/autenticacion/guardas';
import { ROLES_CANDIDATAS, ROLES_CONSOLIDADO } from '../../nucleo/menu';

export const RUTAS_RECONSTRUCCION: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'proyectos' },
  { path: 'candidatas', canMatch: [conRoles(ROLES_CANDIDATAS)], loadComponent: () => import('./paginas/candidatas').then((m) => m.Candidatas) },
  { path: 'proyectos', loadComponent: () => import('./paginas/proyectos').then((m) => m.Proyectos) },
  { path: 'proyectos/:id', loadComponent: () => import('./paginas/proyecto').then((m) => m.DetalleProyecto) },
  { path: 'consolidado', canMatch: [conRoles(ROLES_CONSOLIDADO)], loadComponent: () => import('./paginas/consolidado').then((m) => m.ConsolidadoPagina) },
];
