import { Component } from '@angular/core';
import { Routes } from '@angular/router';

/** Módulo reservado: su contenido (Leaflet con las tres capas) llega con la tarea B2. */
@Component({
  selector: 'app-mapa',
  template: `
    <h1>Mapa</h1>
    <p class="vacio">Este módulo todavía no tiene contenido.</p>
  `,
  styles: `h1 { margin-bottom: 1.5rem; }`,
})
export class MapaInicio {}

export const RUTAS_MAPA: Routes = [{ path: '', component: MapaInicio }];
