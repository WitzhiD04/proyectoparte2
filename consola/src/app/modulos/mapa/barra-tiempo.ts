import { Component, computed, input, linkedSignal, output } from '@angular/core';
import { fechaExacta, nombreEstado, nombreSeveridad, paradaDeVersion, textoCambio, textoZona } from './formato-mapa';
import { CambioDesastre, VersionZona } from './modelos-mapa';

/** Lo que se sabe del desastre en la parada elegida. */
export interface MomentoDesastre {
  severidad: string | null;
  estado: string | null;
  version: number | null;
  municipios: number;
}

/**
 * Barra de tiempo de la zona (RF-GD-17): la misma "vía" de la línea de estados de los proyectos. Lo recorrido va en
 * tinta, lo que sigue punteado y la parada elegida en amarillo de señal.
 */
@Component({
  selector: 'app-barra-tiempo',
  template: `
    <section class="panel" aria-labelledby="titulo-tiempo">
      <div class="cabeza">
        <h2 id="titulo-tiempo">Evolución del desastre</h2>
        @if (cargando()) { <span class="suave" role="status">Consultando ese momento…</span> }
      </div>

      @if (instantes().length === 0) {
        <p class="suave">Este desastre todavía no tiene cambios registrados.</p>
      } @else {
        <div class="via" [style.--avance]="avance()">
          <input
            type="range"
            min="0"
            [max]="instantes().length - 1"
            step="1"
            aria-label="Momento del desastre"
            [attr.aria-valuetext]="textoParada()"
            [value]="vista()"
            [disabled]="instantes().length < 2"
            (input)="vista.set(valor($event))"
            (change)="elegir.emit(valor($event))"
          />
          <ol class="paradas" aria-hidden="true">
            @for (instante of instantes(); track instante; let i = $index) {
              <li [class.pasada]="i <= vista()" [class.actual]="i === vista()"></li>
            }
          </ol>
        </div>

        <div class="detalle">
          <div>
            <p class="cuando">{{ exacta(instanteVista()) }}@if (esUltima()) { <span class="suave"> (último cambio)</span> }</p>
            <ul class="cambios">
              @for (cambio of cambiosVista(); track $index) { <li>{{ texto(cambio) }} <span class="suave">({{ cambio.autor }})</span></li> }
            </ul>
          </div>
          @if (momento(); as m) {
            <dl>
              <div><dt>Severidad</dt><dd>{{ severidad(m.severidad) }}</dd></div>
              <div><dt>Estado</dt><dd>{{ estado(m.estado) }}</dd></div>
              <div><dt>Zona</dt><dd>{{ zona(m.version, m.municipios) }}</dd></div>
            </dl>
          }
        </div>

        @if (versiones().length > 0) {
          <div class="versiones" role="group" aria-label="Versiones de la zona">
            <span>Versiones de la zona:</span>
            @for (v of versiones(); track v.version) {
              <button type="button" class="secundario" [class.activa]="v.version === momento()?.version" [attr.aria-pressed]="v.version === momento()?.version" (click)="irAVersion(v)">
                v{{ v.version }}, {{ exacta(v.vigenteDesde) }}
              </button>
            }
          </div>
        }
      }
    </section>
  `,
  styles: `
    .cabeza { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; align-items: baseline; margin-bottom: 1.1rem; }
    .via { position: relative; padding: 0 0.1rem 1rem; }
    input[type='range'] { appearance: none; display: block; width: 100%; height: 1.5rem; margin: 0; padding: 0; border: 0; background: transparent; cursor: pointer; }
    input::-webkit-slider-runnable-track { height: 6px; background: linear-gradient(90deg, var(--tinta) var(--avance), transparent var(--avance)), repeating-linear-gradient(90deg, var(--linea) 0 6px, transparent 6px 12px); }
    input::-moz-range-track { height: 6px; background: linear-gradient(90deg, var(--tinta) var(--avance), transparent var(--avance)), repeating-linear-gradient(90deg, var(--linea) 0 6px, transparent 6px 12px); }
    input::-webkit-slider-thumb { appearance: none; width: 1.3rem; height: 1.3rem; margin-top: calc(3px - 0.65rem); background: var(--senal); border: 3px solid var(--tinta); border-radius: 0; }
    input::-moz-range-thumb { width: 1.3rem; height: 1.3rem; box-sizing: border-box; background: var(--senal); border: 3px solid var(--tinta); border-radius: 0; }
    input:disabled { cursor: default; }
    .paradas { list-style: none; display: flex; justify-content: space-between; margin: 0.15rem 0 0; padding: 0 0.45rem; }
    .paradas li { width: 0.4rem; height: 0.55rem; background: var(--linea); }
    .paradas li.pasada { background: var(--tinta); }
    .paradas li.actual { background: var(--senal); outline: 1px solid var(--tinta); }
    .detalle { display: grid; grid-template-columns: repeat(auto-fit, minmax(17rem, 1fr)); gap: 0.8rem 2rem; }
    .cuando { margin: 0; font-family: var(--titulo); font-stretch: 75%; font-weight: 700; font-size: 1.25rem; }
    .cambios { margin: 0.2rem 0 0; padding: 0; list-style: none; }
    dl { margin: 0; display: grid; gap: 0.2rem; }
    dl div { display: flex; gap: 0.6rem; }
    dt { min-width: 5.5rem; color: var(--tinta-suave); }
    dd { margin: 0; font-weight: 500; }
    .versiones { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; margin-top: 1rem; }
    .versiones button { padding: 0.2rem 0.6rem; font-size: 0.9rem; }
    .versiones button.activa { background: var(--senal); }
  `,
})
export class BarraTiempo {
  readonly cambios = input.required<readonly CambioDesastre[]>();
  readonly instantes = input.required<readonly string[]>();
  readonly versiones = input.required<readonly VersionZona[]>();
  readonly indice = input.required<number>();
  readonly momento = input<MomentoDesastre | null>(null);
  readonly cargando = input(false);
  readonly elegir = output<number>();

  /** Lo que muestra la barra mientras se arrastra; la consulta sale al soltarla. */
  readonly vista = linkedSignal(() => this.indice());

  readonly instanteVista = computed(() => this.instantes()[this.vista()] ?? null);
  readonly cambiosVista = computed(() => this.cambios().filter((c) => c.instante === this.instanteVista()));
  readonly esUltima = computed(() => this.vista() === this.instantes().length - 1);
  readonly avance = computed(() => {
    const ultima = this.instantes().length - 1;
    return `${ultima > 0 ? (this.vista() / ultima) * 100 : 100}%`;
  });
  readonly textoParada = computed(() => this.cambiosVista().map(textoCambio).join('. '));

  readonly texto = textoCambio;
  readonly severidad = nombreSeveridad;
  readonly estado = nombreEstado;
  readonly exacta = fechaExacta;
  readonly zona = textoZona;

  valor(evento: Event): number {
    return Number((evento.target as HTMLInputElement).value);
  }

  irAVersion(version: VersionZona): void {
    this.elegir.emit(paradaDeVersion(this.instantes(), version));
  }
}
