import { Component, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, leerError } from '../nucleo/api';
import { Candidata, ErrorApi, NOMBRE_DANO } from '../nucleo/modelos';
import { Sesion } from '../nucleo/sesion';

/** CU-01 y CU-02: consultar las evaluaciones candidatas y crear proyectos con las elegidas. */
@Component({
  selector: 'app-candidatas',
  template: `
    <h1>Evaluaciones candidatas</h1>
    <p class="intro">Evaluaciones validadas con daño grave o destrucción total que todavía no tienen proyecto.
      Se crea un proyecto por cada activo.</p>

    @if (error(); as e) {
      <div class="aviso error" role="alert">
        {{ e.mensaje }}
        @if (e.detalles?.length) {
          <ul>@for (d of e.detalles; track d) { <li>{{ d }}</li> }</ul>
        }
      </div>
    }

    @if (candidatas().length > 0) {
      <div class="tabla-ancha">
        <table>
          <thead>
            <tr><th><span class="oculto">Elegir</span></th><th>Activo</th><th>Tipo</th><th>Nivel de daño</th><th>Municipio</th></tr>
          </thead>
          <tbody>
            @for (c of candidatas(); track c.idEvaluacion) {
              <tr>
                <td><input type="checkbox" [id]="c.idEvaluacion" [checked]="elegidas().has(c.idEvaluacion)" (change)="alternar(c.idEvaluacion)" /></td>
                <td><label [for]="c.idEvaluacion">{{ c.nombreActivo }}</label></td>
                <td>{{ c.tipoActivo }}</td>
                <td>{{ danos[c.nivelDano] }}</td>
                <td>{{ c.municipio }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <p class="acciones">
        <button type="button" [disabled]="elegidas().size === 0 || enviando()" (click)="crear()">
          Crear proyectos ({{ elegidas().size }} {{ elegidas().size === 1 ? 'evaluación' : 'evaluaciones' }})
        </button>
      </p>
    } @else if (cargado() && !error()) {
      <p class="vacio">No hay evaluaciones candidatas en este desastre. Todas las que cumplen las condiciones ya tienen proyecto.</p>
    }
  `,
  styles: `
    .intro { max-width: 60ch; margin: 0.6rem 0 1.5rem; }
    td input { width: 1.2rem; height: 1.2rem; }
    td label { margin: 0; font-weight: 600; font-size: 1rem; cursor: pointer; }
    .acciones { margin-top: 1.2rem; }
    .oculto { position: absolute; left: -999rem; }
  `,
})
export class Candidatas {
  private readonly api = inject(Api);
  private readonly sesion = inject(Sesion);
  private readonly router = inject(Router);

  protected readonly danos = NOMBRE_DANO;
  protected readonly candidatas = signal<Candidata[]>([]);
  protected readonly elegidas = signal<Set<string>>(new Set());
  protected readonly error = signal<ErrorApi | null>(null);
  protected readonly cargado = signal(false);
  protected readonly enviando = signal(false);
  // La clave identifica este lote: si la respuesta se pierde y se reenvía, no se crean proyectos repetidos.
  private clave = crypto.randomUUID();

  constructor() {
    effect(() => {
      const idDesastre = this.sesion.idDesastre();
      if (!idDesastre) {
        return;
      }
      this.error.set(null);
      this.elegidas.set(new Set());
      this.api.candidatas(idDesastre).subscribe({
        next: (lista) => { this.candidatas.set(lista); this.cargado.set(true); },
        error: (r) => { this.candidatas.set([]); this.error.set(leerError(r)); this.cargado.set(true); },
      });
    });
  }

  protected alternar(id: string): void {
    const copia = new Set(this.elegidas());
    if (copia.has(id)) { copia.delete(id); } else { copia.add(id); }
    this.elegidas.set(copia);
    this.clave = crypto.randomUUID();
  }

  protected crear(): void {
    this.enviando.set(true);
    this.error.set(null);
    this.api.crearLote(this.sesion.idDesastre(), [...this.elegidas()], this.clave).subscribe({
      next: () => this.router.navigate(['/proyectos']),
      error: (r) => { this.error.set(leerError(r)); this.enviando.set(false); },
    });
  }
}
