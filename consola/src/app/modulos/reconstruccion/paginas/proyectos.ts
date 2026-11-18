import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, leerError } from '../../../nucleo/api';
import { PesosPipe } from '../../../nucleo/formato';
import { ErrorApi, NOMBRE_ESTADO, Proyecto } from '../../../nucleo/modelos';
import { Sesion } from '../../../nucleo/sesion';

@Component({
  selector: 'app-proyectos',
  imports: [RouterLink, PesosPipe],
  template: `
    <h1>Proyectos de reconstrucción</h1>
    @if (sesion.esResponsable()) {
      <p class="intro">Proyectos en los que usted es el responsable asignado.</p>
    }

    @if (error(); as e) { <p class="aviso error" role="alert">{{ e.mensaje }}</p> }

    @if (visibles().length > 0) {
      <div class="tabla-ancha">
        <table>
          <thead>
            <tr><th>Proyecto</th><th>Estado</th><th>Responsable</th><th class="numero">Presupuesto</th><th class="numero">Costo ejecutado</th><th class="numero">Avance</th></tr>
          </thead>
          <tbody>
            @for (p of visibles(); track p.id) {
              <tr>
                <td><a [routerLink]="['/reconstruccion/proyectos', p.id]">{{ p.nombre }}</a><br /><span class="suave">{{ p.alcance }}</span></td>
                <td><span class="estado" [class]="p.estado">{{ estados[p.estado] }}</span></td>
                <td>{{ p.responsable ?? 'Sin asignar' }}</td>
                <td class="numero">{{ p.presupuesto | pesos }}</td>
                <td class="numero" [class.sobrecosto]="p.enSobrecosto">{{ p.costoEjecutado | pesos }}@if (p.enSobrecosto) {<br />En sobrecosto}</td>
                <td class="numero">{{ p.avance }} %</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @else if (cargado() && !error()) {
      <p class="vacio">
        @if (sesion.esAutoridad()) {
          Todavía no hay proyectos en este desastre. <a routerLink="/reconstruccion/candidatas">Cree los primeros a partir de las evaluaciones candidatas.</a>
        } @else if (sesion.esResponsable()) {
          No tiene proyectos asignados en este desastre.
        } @else {
          Todavía no hay proyectos en este desastre.
        }
      </p>
    }
  `,
  styles: `
    .intro { margin: 0.6rem 0 0; }
    h1 { margin-bottom: 1.5rem; }
    td a { font-weight: 600; }
  `,
})
export class Proyectos {
  private readonly api = inject(Api);
  protected readonly sesion = inject(Sesion);
  protected readonly estados = NOMBRE_ESTADO;

  protected readonly proyectos = signal<Proyecto[]>([]);
  protected readonly error = signal<ErrorApi | null>(null);
  protected readonly cargado = signal(false);

  protected readonly visibles = computed(() => {
    const usuario = this.sesion.usuario();
    return this.sesion.esResponsable() ? this.proyectos().filter((p) => p.responsable === usuario) : this.proyectos();
  });

  constructor() {
    // Se vuelve a cargar cada vez que cambia el desastre elegido.
    effect(() => {
      const idDesastre = this.sesion.idDesastre();
      if (!idDesastre) {
        return;
      }
      this.api.proyectos(idDesastre).subscribe({
        next: (lista) => { this.proyectos.set(lista); this.cargado.set(true); },
        error: (r) => { this.error.set(leerError(r)); this.cargado.set(true); },
      });
    });
  }
}
