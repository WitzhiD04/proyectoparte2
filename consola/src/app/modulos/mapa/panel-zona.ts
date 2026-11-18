import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { leerError } from '../../nucleo/api';
import { FechaHoraPipe } from '../../nucleo/formato';
import { ApiMapa } from './api-mapa';
import { COLOR_NIVEL, NOMBRE_NIVEL, cantidad, legible, nombreOrigen, nombreValidacion } from './formato-mapa';
import { DiaZona, EvolucionZona, NIVELES, NivelDano } from './modelos-mapa';

const MAXIMO_EVALUACIONES = 50;

/** Evolución de una zona (RF-ED-15, E-40): activos por nivel en cada día y las evaluaciones más recientes. */
@Component({
  selector: 'app-panel-zona',
  imports: [FechaHoraPipe],
  template: `
    <section class="panel" aria-labelledby="titulo-zona">
      <div class="cabeza">
        <h2 id="titulo-zona" #titulo tabindex="-1">{{ evolucion()?.zona?.nombre ?? 'Municipio ' + codigo() }}</h2>
        <button type="button" class="secundario" (click)="cerrar.emit()">Volver a las capas</button>
      </div>
      @if (error(); as mensaje) { <p class="aviso error" role="alert">{{ mensaje }}</p> }
      @if (degradada()) { <p class="aviso degradado" role="status">Es la última respuesta conocida.</p> }

      @if (evolucion(); as e) {
        <p class="suave dato">Dato al {{ e.datoAl | fechaHora }}</p>
        <h3>Activos por día</h3>
        <ul class="dias">
          @for (d of e.porDia; track d.fecha) {
            <li>
              <div class="dia"><strong>{{ fecha(d.fecha) }}</strong> <span class="suave">{{ total(d.activos) }}</span></div>
              <div class="barra" aria-hidden="true">
                @for (n of presentes(d); track n) { <span [style.flex-grow]="d.distribucion[n]" [style.background]="color(n)"></span> }
              </div>
              <p class="suave reparto">{{ reparto(d) }}</p>
            </li>
          }
        </ul>
        <h3>Evaluaciones recientes</h3>
        <ol class="secuencia">
          @for (h of recientes(); track $index) {
            <li>
              <span class="muestra" [style.background]="color(h.nivelDano)"></span>
              <div>
                <strong>{{ nombreNivel(h.nivelDano) }}</strong>, {{ tipo(h.tipoActivo) }}
                <div class="suave">{{ origen(h.origen) }}@if (h.estadoValidacion) { ({{ validacion(h.estadoValidacion).toLowerCase() }}) }, {{ h.instante | fechaHora }}</div>
              </div>
            </li>
          }
        </ol>
      } @else if (!error()) {
        <p class="suave" role="status">Consultando el municipio…</p>
      }
    </section>
  `,
  styles: `
    .cabeza { display: flex; gap: 0.75rem; justify-content: space-between; align-items: start; margin-bottom: 0.4rem; }
    .cabeza h2:focus { outline: none; }
    .cabeza button { padding: 0.2rem 0.55rem; font-size: 0.85rem; flex: none; }
    .aviso.degradado { border-color: var(--senal); }
    .dato { margin: 0; font-size: 0.92rem; }
    h3 { margin: 1rem 0 0.5rem; }
    .dias, .secuencia { list-style: none; margin: 0; padding: 0; }
    .dias li { margin-bottom: 0.75rem; font-size: 0.92rem; }
    .dia { display: flex; justify-content: space-between; gap: 0.5rem; }
    .barra { display: flex; gap: 2px; height: 0.8rem; margin: 0.2rem 0; }
    .barra span { flex-basis: 0; min-width: 3px; }
    .reparto { margin: 0; font-size: 0.85rem; }
    .secuencia { max-height: 18rem; overflow-y: auto; }
    .secuencia li { display: flex; gap: 0.55rem; padding: 0.4rem 0; border-top: 1px solid var(--linea); font-size: 0.9rem; }
    .muestra { flex: none; width: 0.85rem; height: 0.85rem; margin-top: 0.25rem; border: 1px solid var(--tinta); }
  `,
})
export class PanelZona {
  private readonly api = inject(ApiMapa);
  private readonly titulo = viewChild.required<ElementRef<HTMLElement>>('titulo');

  readonly codigo = input.required<string>();
  readonly desastre = input.required<string>();
  readonly cerrar = output<void>();

  readonly evolucion = signal<EvolucionZona | null>(null);
  readonly degradada = signal(false);
  readonly error = signal<string | null>(null);
  readonly recientes = computed(() => [...(this.evolucion()?.evaluaciones ?? [])].reverse().slice(0, MAXIMO_EVALUACIONES));

  readonly tipo = legible;
  readonly origen = nombreOrigen;
  readonly validacion = nombreValidacion;
  readonly nombreNivel = (nivel: NivelDano) => NOMBRE_NIVEL[nivel];
  readonly color = (nivel: NivelDano) => COLOR_NIVEL[nivel];
  readonly total = (n: number) => cantidad(n, 'activo');
  readonly fecha = (f: string) => f.split('-').reverse().join('/');
  readonly presentes = (d: DiaZona) => NIVELES.filter((n) => d.distribucion[n] > 0);
  readonly reparto = (d: DiaZona) => this.presentes(d).map((n) => `${NOMBRE_NIVEL[n]}: ${d.distribucion[n]}`).join(', ') || 'Sin activos contados';

  constructor() {
    effect((alLimpiar) => {
      const codigo = this.codigo();
      const desastre = this.desastre();
      this.evolucion.set(null);
      this.degradada.set(false);
      this.error.set(null);
      this.titulo().nativeElement.focus({ preventScroll: true });
      const lectura = this.api.evolucionZona(codigo, desastre).subscribe({
        next: ({ cuerpo, degradado }) => {
          this.evolucion.set(cuerpo);
          this.degradada.set(degradado);
        },
        error: (e: HttpErrorResponse) => this.error.set(leerError(e).mensaje),
      });
      alLimpiar(() => lectura.unsubscribe());
    });
  }
}
