import { Component, effect, inject, signal } from '@angular/core';
import { Api, leerError } from '../../../nucleo/api';
import { PesosPipe } from '../../../nucleo/formato';
import { Consolidado, ErrorApi, Estado, NOMBRE_ESTADO } from '../../../nucleo/modelos';
import { Sesion } from '../../../nucleo/sesion';

/** CU-11: consolidado del programa de un desastre. Lo sirve el servicio de Consulta del Programa. */
@Component({
  selector: 'app-consolidado',
  imports: [PesosPipe],
  template: `
    <h1>Consolidado del programa</h1>
    @if (error(); as e) { <p class="aviso error" role="alert">{{ e.mensaje }}</p> }

    @if (datos(); as c) {
      @if (c.totalProyectos === 0) {
        <p class="vacio">Este desastre todavía no tiene proyectos de reconstrucción.</p>
      } @else {
        <p class="aviso" [class.ok]="c.todosEnEstadoFinal">
          @if (c.todosEnEstadoFinal) {
            Todos los proyectos están finalizados o cancelados: el desastre puede cerrarse.
          } @else {
            Hay proyectos abiertos: el desastre todavía no puede cerrarse.
          }
        </p>

        <section class="panel">
          <h2>Avance del programa</h2>
          <div class="barra" role="img" [attr.aria-label]="'Avance agregado ' + c.avanceAgregado + ' por ciento'">
            <div class="hecho" [style.width.%]="c.avanceAgregado"></div>
          </div>
          <p class="cifra">{{ c.avanceAgregado }} %</p>
          <p class="suave">Promedio de los avances ponderado por presupuesto, sin contar los proyectos cancelados.</p>
        </section>

        <div class="dos">
          <section class="panel">
            <h2>Proyectos por estado</h2>
            <table>
              <tbody>
                @for (estado of orden; track estado) {
                  <tr><td><span class="estado" [class]="estado">{{ nombres[estado] }}</span></td><td class="numero">{{ c.proyectosPorEstado[estado] }}</td></tr>
                }
                <tr><th>Total</th><th class="numero">{{ c.totalProyectos }}</th></tr>
              </tbody>
            </table>
          </section>

          <section class="panel">
            <h2>Dinero</h2>
            <table>
              <tbody>
                <tr><td>Presupuesto asignado</td><td class="numero">{{ c.presupuestoAsignado | pesos }}</td></tr>
                <tr><td>Costo ejecutado</td><td class="numero">{{ c.costoEjecutado | pesos }}</td></tr>
                <tr>
                  <td [class.sobrecosto]="c.proyectosEnSobrecosto.length > 0">Proyectos en sobrecosto</td>
                  <td class="numero" [class.sobrecosto]="c.proyectosEnSobrecosto.length > 0">{{ c.proyectosEnSobrecosto.length }}</td>
                </tr>
              </tbody>
            </table>
          </section>
        </div>
      }
    }
  `,
  styles: `
    h1 { margin-bottom: 1.5rem; }
    .dos { display: grid; grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr)); gap: 1.5rem; }
    .barra { height: 1.6rem; border: 2px solid var(--tinta); background: repeating-linear-gradient(90deg, var(--plano) 0 0.5rem, var(--papel) 0.5rem 1rem); }
    .hecho { height: 100%; background: var(--senal); border-right: 2px solid var(--tinta); }
    .cifra { font-family: var(--titulo); font-stretch: 62%; font-weight: 800; font-size: 3.2rem; line-height: 1; margin: 0.6rem 0 0.2rem; }
    .suave { margin: 0; }
  `,
})
export class ConsolidadoPagina {
  private readonly api = inject(Api);
  private readonly sesion = inject(Sesion);
  protected readonly nombres = NOMBRE_ESTADO;
  protected readonly orden: Estado[] = ['Formulado', 'Aprobado', 'EnEjecucion', 'Finalizado', 'Cancelado'];

  protected readonly datos = signal<Consolidado | null>(null);
  protected readonly error = signal<ErrorApi | null>(null);

  constructor() {
    effect(() => {
      const idDesastre = this.sesion.idDesastre();
      if (!idDesastre) {
        return;
      }
      this.error.set(null);
      this.api.consolidado(idDesastre).subscribe({
        next: (c) => this.datos.set(c),
        error: (r) => { this.datos.set(null); this.error.set(leerError(r)); },
      });
    });
  }
}
