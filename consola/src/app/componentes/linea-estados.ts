import { Component, computed, input } from '@angular/core';
import { Estado, NOMBRE_ESTADO } from '../nucleo/modelos';

/**
 * Línea de obra: el recorrido del proyecto dibujado como una vía. Lo recorrido queda en tinta, el tramo
 * actual en amarillo de señal y lo que falta punteado. Un proyecto cancelado corta la vía.
 */
@Component({
  selector: 'app-linea-estados',
  template: `
    <ol class="via" [class.cortada]="estado() === 'Cancelado'" [attr.aria-label]="'Estado actual: ' + nombres[estado()]">
      @for (paso of pasos(); track paso.estado) {
        <li [class.hecho]="paso.hecho" [class.actual]="paso.actual" [class.llegada]="paso.actual && paso.estado === 'Finalizado'" [attr.aria-current]="paso.actual ? 'step' : null">
          <span class="hito"></span>
          <span class="nombre">{{ nombres[paso.estado] }}</span>
        </li>
      }
    </ol>
    @if (estado() === 'Cancelado') {
      <p class="corte">Proyecto cancelado: la obra no continúa.</p>
    }
  `,
  styles: `
    .via { list-style: none; display: grid; grid-template-columns: repeat(4, 1fr); margin: 0; padding: 0; }
    li { position: relative; padding-top: 1.6rem; font-family: var(--titulo); font-stretch: 75%; font-weight: 600; color: var(--tinta-suave); }
    li::before { content: ''; position: absolute; top: 0.45rem; left: 0; right: 0; border-top: 6px dotted var(--linea); }
    li.hecho::before { border-top: 6px solid var(--tinta); }
    li.actual::before { border-top: 6px solid var(--senal); }
    .hito { position: absolute; top: 0; left: 0; width: 1.15rem; height: 1.15rem; background: var(--papel); border: 3px solid var(--linea); }
    li.hecho .hito { background: var(--tinta); border-color: var(--tinta); }
    li.actual .hito { background: var(--senal); border-color: var(--tinta); }
    li.llegada::before { border-top-color: var(--via); }
    li.llegada .hito { background: var(--via); }
    li.hecho, li.actual { color: var(--tinta); }
    li.actual .nombre { font-size: 1.25rem; }
    .via.cortada li::before { border-top-style: dashed; border-top-color: var(--oxido); }
    .via.cortada .hito { border-color: var(--oxido); background: var(--papel); }
    .corte { margin: 0.6rem 0 0; color: var(--oxido); font-weight: 600; }
    @media (max-width: 34rem) { .via { grid-template-columns: 1fr 1fr; row-gap: 0.8rem; } }
  `,
})
export class LineaEstados {
  readonly estado = input.required<Estado>();
  protected readonly nombres = NOMBRE_ESTADO;
  private readonly orden: Estado[] = ['Formulado', 'Aprobado', 'EnEjecucion', 'Finalizado'];

  protected readonly pasos = computed(() => {
    const posicion = this.orden.indexOf(this.estado());
    return this.orden.map((estado, i) => ({ estado, hecho: posicion > i, actual: posicion === i }));
  });
}
