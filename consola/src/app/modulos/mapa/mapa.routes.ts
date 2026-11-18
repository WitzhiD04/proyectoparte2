import { Routes } from '@angular/router';

export const RUTAS_MAPA: Routes = [{ path: '', loadComponent: () => import('./mapa').then((m) => m.PantallaMapa) }];
