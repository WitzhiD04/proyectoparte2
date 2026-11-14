import { Routes } from '@angular/router';
import { soloRoles } from './nucleo/api';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'proyectos' },
  { path: 'candidatas', canActivate: [soloRoles('Autoridad')], loadComponent: () => import('./paginas/candidatas').then((m) => m.Candidatas) },
  { path: 'proyectos', loadComponent: () => import('./paginas/proyectos').then((m) => m.Proyectos) },
  { path: 'proyectos/:id', loadComponent: () => import('./paginas/proyecto').then((m) => m.DetalleProyecto) },
  { path: 'consolidado', canActivate: [soloRoles('Autoridad', 'Auditor')], loadComponent: () => import('./paginas/consolidado').then((m) => m.ConsolidadoPagina) },
  { path: '**', redirectTo: 'proyectos' },
];
