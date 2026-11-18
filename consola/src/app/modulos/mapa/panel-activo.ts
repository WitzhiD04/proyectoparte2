import { Component, ElementRef, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { leerError } from '../../nucleo/api';
import { FechaHoraPipe } from '../../nucleo/formato';
import { ApiMapa } from './api-mapa';
import { COLOR_NIVEL, NOMBRE_NIVEL, legible, nombreOrigen, nombreValidacion } from './formato-mapa';
import { EvolucionActivo, NivelDano } from './modelos-mapa';

/** Evolución de un activo (RF-ED-15): su estado vigente y la secuencia de sus evaluaciones. */
@Component({
  selector: 'app-panel-activo',
  imports: [FechaHoraPipe],
  template: `
    <section class="panel" aria-labelledby="titulo-activo">
      <div class="cabeza">
        <h2 id="titulo-activo" #titulo tabindex="-1">{{ evolucion() ? tipo() : 'Activo' }}</h2>
        <button type="button" class="secundario" (click)="cerrar.emit()">Volver a las capas</button>
      </div>

      @if (error(); as mensaje) { <p class="aviso error" role="alert">{{ mensaje }}</p> }

      @if (evolucion(); as e) {
        <p class="nivel">
          @if (e.vigente.nivelDano; as nivel) { <span class="muestra" [style.background]="color(nivel)"></span>{{ nombreNivel(nivel) }} }
        </p>
        <p class="suave resumen">
          {{ validacion(e.vigente.estadoValidacion) }}. Municipio {{ e.vigente.codigoMunicipio }}.
          @if (e.vigente.conflictoPendiente) { <strong class="conflicto">Tiene un conflicto pendiente.</strong> }
          @if (e.vigente.fusionado) { Fusionado en otro activo. }
        </p>
        <p class="suave dato">Dato al {{ e.datoAl | fechaHora }}</p>

        <h3>Evaluaciones</h3>
        <ol class="secuencia">
          @for (h of e.evaluaciones; track $index) {
            <li>
              <span class="muestra" [style.background]="color(h.nivelDano)"></span>
              <div>
                <strong>{{ nombreNivel(h.nivelDano) }}</strong>, {{ origen(h.origen) }}
                @if (h.origen === 'VALIDACION') { ({{ validacion(h.estadoValidacion).toLowerCase() }}) }
                <div class="suave">{{ h.instante | fechaHora }}@if (h.autor) {, {{ h.autor }}}</div>
                @if (h.enConflicto) { <div class="conflicto">Llegó en conflicto</div> }
              </div>
            </li>
          }
        </ol>
      } @else if (!error()) {
        <p class="suave" role="status">Consultando el activo…</p>
      }
    </section>
  `,
  styles: `
    .cabeza { display: flex; gap: 0.75rem; justify-content: space-between; align-items: start; margin-bottom: 0.6rem; }
    .cabeza h2:focus { outline: none; }
    .cabeza button { padding: 0.2rem 0.55rem; font-size: 0.85rem; flex: none; }
    .nivel { display: flex; gap: 0.5rem; align-items: center; margin: 0; font-family: var(--titulo); font-stretch: 75%; font-weight: 700; font-size: 1.3rem; }
    .resumen, .dato { margin: 0.2rem 0 0; font-size: 0.92rem; }
    h3 { margin: 1rem 0 0.5rem; }
    .muestra { flex: none; display: inline-block; width: 0.9rem; height: 0.9rem; border: 1px solid var(--tinta); }
    .secuencia { list-style: none; margin: 0; padding: 0; max-height: 24rem; overflow-y: auto; }
    .secuencia li { display: flex; gap: 0.55rem; padding: 0.45rem 0; border-top: 1px solid var(--linea); font-size: 0.92rem; }
    .secuencia .muestra { margin-top: 0.3rem; }
    .conflicto { color: var(--oxido); font-weight: 600; }
  `,
})
export class PanelActivo {
  private readonly api = inject(ApiMapa);
  private readonly titulo = viewChild.required<ElementRef<HTMLElement>>('titulo');

  readonly idActivo = input.required<string>();
  readonly cerrar = output<void>();

  readonly evolucion = signal<EvolucionActivo | null>(null);
  readonly error = signal<string | null>(null);
  readonly tipo = computed(() => {
    const tipo = this.evolucion()?.vigente.tipoActivo;
    return tipo ? legible(tipo) : 'Activo';
  });

  readonly validacion = nombreValidacion;
  readonly origen = nombreOrigen;
  nombreNivel = (nivel: NivelDano) => NOMBRE_NIVEL[nivel];
  color = (nivel: NivelDano) => COLOR_NIVEL[nivel];

  constructor() {
    effect((alLimpiar) => {
      const id = this.idActivo();
      this.evolucion.set(null);
      this.error.set(null);
      this.titulo().nativeElement.focus({ preventScroll: true });
      const lectura = this.api.evolucionActivo(id).subscribe({
        next: (r) => this.evolucion.set(r.cuerpo),
        error: (e: HttpErrorResponse) => this.error.set(leerError(e).mensaje),
      });
      alLimpiar(() => lectura.unsubscribe());
    });
  }
}
