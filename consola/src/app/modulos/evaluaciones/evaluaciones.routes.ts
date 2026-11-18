import { Component } from '@angular/core';
import { Routes } from '@angular/router';

/** Módulo reservado: su contenido (validación, conflictos y catálogo) llega con la tarea B3. */
@Component({
  selector: 'app-evaluaciones',
  template: `
    <h1>Evaluaciones</h1>
    <p class="vacio">Este módulo todavía no tiene contenido.</p>
  `,
  styles: `h1 { margin-bottom: 1.5rem; }`,
})
export class EvaluacionesInicio {}

export const RUTAS_EVALUACIONES: Routes = [{ path: '', component: EvaluacionesInicio }];
