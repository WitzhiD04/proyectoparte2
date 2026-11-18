import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { leerError } from '../../nucleo/api';
import { ApiEvaluaciones } from './api-evaluaciones';
import { DECISIONES, compararVersiones, fechaExacta, nombreDecision, razonNoDisponible } from './formato-evaluaciones';
import { Conflicto, DecisionConflicto, EleccionVigente, EstadoConflicto } from './modelos-evaluaciones';

/** Bandeja de conflictos (RF-ED-13, AD-32, AD-33): las dos versiones lado a lado y la decisión humana con su justificación. */
@Component({
  selector: 'app-conflictos',
  template: `
    <h1>Bandeja de conflictos</h1>
    @if (errorLista(); as e) { <p class="aviso error" role="alert">{{ e }}</p> }
    @if (confirmacion(); as c) { <p class="aviso ok" role="status">{{ c }}</p> }

    <form class="mandos" (submit)="$event.preventDefault()">
      <div class="campo">
        <label for="con-estado">Mostrar</label>
        <select id="con-estado" (change)="cambiarEstado($any(valor($event)))">
          <option value="PENDIENTE" [selected]="estado() === 'PENDIENTE'">Pendientes</option>
          <option value="RESUELTO" [selected]="estado() === 'RESUELTO'">Resueltos</option>
        </select>
      </div>
      <button type="button" class="secundario" (click)="recargar()">Actualizar lista</button>
    </form>

    @if (conflictos().length === 0) {
      <p class="vacio">{{ cargando() ? 'Consultando…' : (estado() === 'PENDIENTE' ? 'No hay conflictos pendientes.' : 'Todavía no hay conflictos resueltos.') }}</p>
    } @else {
      <section class="panel" aria-labelledby="titulo-bandeja">
        <h2 id="titulo-bandeja">{{ estado() === 'PENDIENTE' ? 'Pendientes' : 'Resueltos' }} ({{ conflictos().length }})</h2>
        <div class="tabla-ancha" tabindex="0" role="region" aria-labelledby="titulo-bandeja">
          <table>
            <thead><tr><th>Tipo</th><th>Detectado</th><th>Activo</th><th>{{ estado() === 'PENDIENTE' ? 'Regla' : 'Decisión' }}</th><th><span class="oculto">Acción</span></th></tr></thead>
            <tbody>
              @for (c of conflictos(); track c.id) {
                <tr [class.elegida]="c.id === elegido()?.id">
                  <td>{{ nombreTipo(c) }}</td>
                  <td class="sin-corte">{{ fecha(c.instante) }}</td>
                  <td>{{ referencia(c) }}</td>
                  <td>{{ c.decision ? decisionTexto(c.decision) : c.regla }}</td>
                  <td><button type="button" class="secundario" (click)="elegir(c)">Abrir</button></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }

    @if (elegido(); as c) {
      <section class="panel" aria-labelledby="titulo-comparacion">
        <h2 id="titulo-comparacion" #titulo tabindex="-1">{{ nombreTipo(c) }}: {{ referencia(c) }}</h2>
        <p class="regla">{{ c.regla }}</p>
        <div class="tabla-ancha" tabindex="0" role="region" aria-labelledby="titulo-comparacion">
          <table class="comparacion">
            <thead><tr><th scope="col">Campo</th><th scope="col">Versión vigente</th><th scope="col">Versión entrante</th></tr></thead>
            <tbody>
              @for (f of filas(); track f.etiqueta) {
                <tr [class.distinta]="f.distinto">
                  <th scope="row">{{ f.etiqueta }}@if (f.distinto) { <span class="marca">distinto</span> }</th>
                  <td>{{ f.vigente }}</td>
                  <td>{{ f.entrante }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        @if (c.estado === 'RESUELTO') {
          <dl class="resuelto">
            <div><dt>Decisión</dt><dd>{{ c.decision ? decisionTexto(c.decision) : 'Sin dato' }}</dd></div>
            <div><dt>Decidió</dt><dd>{{ c.decididoPor }}</dd></div>
            <div><dt>Cuándo</dt><dd>{{ c.instanteResolucion ? fecha(c.instanteResolucion) : 'Sin dato' }}</dd></div>
            <div><dt>Justificación</dt><dd>{{ c.justificacion }}</dd></div>
          </dl>
        } @else {
          <form class="decision" (submit)="$event.preventDefault(); resolver(c)">
            <fieldset>
              <legend>Decisión</legend>
              @for (d of decisiones; track d.valor) {
                <label class="opcion" [class.apagada]="razon(c, d.valor)">
                  <input type="radio" name="decision" [value]="d.valor" [checked]="decision() === d.valor" [disabled]="razon(c, d.valor) !== null" (change)="decision.set(d.valor)" />
                  <span><strong>{{ d.nombre }}</strong><br /><span class="suave">{{ razon(c, d.valor) ?? d.ayuda }}</span></span>
                </label>
              }
            </fieldset>
            @if (decision() === 'FUSIONAR') {
              <fieldset>
                <legend>Evaluación que queda vigente</legend>
                <label class="opcion"><input type="radio" name="vigente" value="VIGENTE" [checked]="vigente() === 'VIGENTE'" (change)="vigente.set('VIGENTE')" /> <span>La vigente</span></label>
                <label class="opcion"><input type="radio" name="vigente" value="ENTRANTE" [checked]="vigente() === 'ENTRANTE'" (change)="vigente.set('ENTRANTE')" /> <span>La entrante</span></label>
              </fieldset>
            }
            <div class="campo">
              <label for="con-justificacion">Justificación</label>
              <textarea id="con-justificacion" rows="3" maxlength="2000" [value]="justificacion()" (input)="justificacion.set(valor($event))"></textarea>
            </div>
            @if (errorAccion(); as e) { <p class="aviso error" role="alert">{{ e }}</p> }
            <button type="submit" [disabled]="enviando()">Registrar decisión</button>
          </form>
        }
      </section>
    }
  `,
  styles: `
    h1 { margin-bottom: 1.25rem; }
    h2:focus { outline: none; }
    .mandos { display: flex; flex-wrap: wrap; gap: 0.9rem 1.25rem; align-items: end; margin-bottom: 1.25rem; }
    .mandos .campo { margin: 0; min-width: 14rem; }
    td button { padding: 0.2rem 0.55rem; font-size: 0.85rem; }
    tr.elegida td { background: var(--plano); }
    .sin-corte { white-space: nowrap; }
    .regla { margin: 0 0 0.9rem; color: var(--tinta-suave); overflow-wrap: anywhere; }
    .comparacion th[scope='row'] { width: 24%; font-family: inherit; font-stretch: normal; font-weight: 500; font-size: 0.95rem; border-bottom: 1px solid var(--linea); }
    .comparacion td { width: 38%; overflow-wrap: anywhere; }
    tr.distinta td { background: #fff6d6; font-weight: 500; }
    .marca { display: block; color: var(--oxido); font-size: 0.8rem; font-weight: 600; }
    .decision { margin-top: 1.25rem; max-width: 46rem; }
    fieldset { border: 0; padding: 0; margin: 0 0 1rem; }
    legend { font-family: var(--titulo); font-stretch: 75%; font-weight: 700; font-size: 1.15rem; padding: 0; margin-bottom: 0.4rem; }
    .opcion { display: flex; gap: 0.55rem; align-items: start; margin-bottom: 0.5rem; font-size: 1rem; font-weight: 400; cursor: pointer; }
    .opcion input { flex: none; width: 1.05rem; height: 1.05rem; margin-top: 0.2rem; accent-color: var(--tinta); }
    .opcion.apagada { opacity: 0.55; cursor: not-allowed; }
    .resuelto { margin: 1.25rem 0 0; display: grid; gap: 0.3rem; }
    .resuelto div { display: flex; gap: 0.75rem; }
    .resuelto dt { min-width: 7rem; color: var(--tinta-suave); }
    .resuelto dd { margin: 0; font-weight: 500; }
    .oculto { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
  `,
})
export class Conflictos {
  private readonly api = inject(ApiEvaluaciones);
  private readonly titulo = viewChild<ElementRef<HTMLElement>>('titulo');

  readonly decisiones = DECISIONES;
  readonly estado = signal<EstadoConflicto>('PENDIENTE');
  private readonly recargas = signal(0);
  readonly conflictos = signal<Conflicto[]>([]);
  readonly cargando = signal(false);
  readonly errorLista = signal<string | null>(null);
  readonly confirmacion = signal<string | null>(null);
  readonly idElegido = signal<string | null>(null);
  readonly decision = signal<DecisionConflicto | null>(null);
  readonly vigente = signal<EleccionVigente>('VIGENTE');
  readonly justificacion = signal('');
  readonly enviando = signal(false);
  readonly errorAccion = signal<string | null>(null);

  readonly elegido = computed(() => this.conflictos().find((c) => c.id === this.idElegido()) ?? null);
  readonly filas = computed(() => {
    const c = this.elegido();
    return c ? compararVersiones(c) : [];
  });

  readonly fecha = fechaExacta;
  readonly decisionTexto = nombreDecision;
  readonly razon = razonNoDisponible;
  readonly nombreTipo = (c: Conflicto) => (c.tipo === 'VERSION' ? 'Conflicto de versión' : 'Posible duplicado');

  constructor() {
    // La comparación aparece debajo de la lista: el foco lleva hasta ella a quien usa el teclado o un lector de pantalla.
    effect(() => {
      if (this.idElegido()) {
        this.titulo()?.nativeElement.focus();
      }
    });
    effect((alLimpiar) => {
      const estado = this.estado();
      this.recargas();
      this.cargando.set(true);
      this.errorLista.set(null);
      const lectura = this.api.conflictos(estado).subscribe({
        next: (lista) => {
          this.conflictos.set(lista);
          this.cargando.set(false);
        },
        error: (e: HttpErrorResponse) => {
          this.conflictos.set([]);
          this.errorLista.set(leerError(e).mensaje);
          this.cargando.set(false);
        },
      });
      alLimpiar(() => lectura.unsubscribe());
    });
  }

  referencia(c: Conflicto): string {
    const nuevo = c.entrante.contenido['activoNuevo'] as { referencia?: string } | undefined;
    return nuevo?.referencia ?? `Activo ${c.activoId.slice(0, 8)}`;
  }

  valor(evento: Event): string {
    return (evento.target as HTMLInputElement).value;
  }

  cambiarEstado(estado: EstadoConflicto): void {
    this.idElegido.set(null);
    this.confirmacion.set(null);
    this.conflictos.set([]);
    this.estado.set(estado);
  }

  recargar(): void {
    this.recargas.update((n) => n + 1);
  }

  elegir(c: Conflicto): void {
    this.idElegido.set(c.id);
    this.decision.set(null);
    this.vigente.set('VIGENTE');
    this.justificacion.set('');
    this.errorAccion.set(null);
    this.confirmacion.set(null);
  }

  resolver(c: Conflicto): void {
    const decision = this.decision();
    const justificacion = this.justificacion().trim();
    if (!decision) {
      this.errorAccion.set('Elija una decisión.');
      return;
    }
    if (!justificacion) {
      this.errorAccion.set('La decisión necesita una justificación.');
      return;
    }
    this.enviando.set(true);
    this.errorAccion.set(null);
    this.api.resolver(c.id, { decision, justificacion, ...(decision === 'FUSIONAR' ? { vigente: this.vigente() } : {}) }).subscribe({
      next: () => {
        this.enviando.set(false);
        this.conflictos.update((lista) => lista.filter((x) => x.id !== c.id));
        this.idElegido.set(null);
        this.confirmacion.set(`Decisión registrada: ${nombreDecision(decision)}.`);
      },
      error: (e: HttpErrorResponse) => {
        this.enviando.set(false);
        this.errorAccion.set(leerError(e).mensaje);
      },
    });
  }
}
