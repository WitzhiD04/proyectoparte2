import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { leerError } from '../../nucleo/api';
import { FechaHoraPipe } from '../../nucleo/formato';
import { ApiMapa } from '../mapa/api-mapa';
import { COLOR_NIVEL, NOMBRE_NIVEL, legible, nombreValidacion } from '../mapa/formato-mapa';
import { ActivoMapa, FilaDesastre, MapaDanos, NivelDano, ZonaDano } from '../mapa/modelos-mapa';
import { ApiEvaluaciones } from './api-evaluaciones';
import { EstadoValidacion } from './modelos-evaluaciones';

type FiltroEstado = 'PENDIENTES' | 'VALIDADAS' | 'TODAS';

interface FilaEvaluacion {
  idEvaluacion: string;
  tipo: string;
  municipio: string;
  nivel: NivelDano;
  peso: number;
  estado: string | null;
  conflicto: boolean;
  datoAl: string;
}

const MAXIMO_FILAS = 200;
/** La lista sale del modelo de lectura, que se entera de la validación por un evento. */
const ESPERA_ACTUALIZACION_MS = 1500;

/**
 * Validación de evaluaciones (RF-ED-08). Evaluación de Daños no lista por desastre, así que las filas vienen del modelo de
 * lectura de Consulta de Daños, el mismo del mapa. La decisión va a Evaluación de Daños, que aplica la regla de nivel 2.
 */
@Component({
  selector: 'app-validacion',
  imports: [FechaHoraPipe],
  template: `
    <h1>Validación de evaluaciones</h1>
    @if (errorLista(); as e) { <p class="aviso error" role="alert">{{ e }}</p> }
    @if (degradada()) { <p class="aviso degradado" role="status">La lista es la última conocida: el servicio no pudo actualizarla.</p> }
    @if (confirmacion(); as c) { <p class="aviso ok" role="status">{{ c }}</p> }

    <form class="mandos" (submit)="$event.preventDefault()">
      <div class="campo">
        <label for="val-desastre">Desastre</label>
        <select id="val-desastre" [disabled]="desastres().length === 0" (change)="elegirDesastre(valor($event))">
          @for (d of desastres(); track d.id) { <option [value]="d.id" [selected]="d.id === idDesastre()">{{ d.nombre ?? 'Desastre sin nombre' }}{{ d.estado === 'CERRADO' ? ' (cerrado)' : '' }}</option> }
        </select>
      </div>
      <div class="campo">
        <label for="val-estado">Mostrar</label>
        <select id="val-estado" (change)="filtro.set($any(valor($event)))">
          <option value="PENDIENTES" [selected]="filtro() === 'PENDIENTES'">Sin validar</option>
          <option value="VALIDADAS" [selected]="filtro() === 'VALIDADAS'">Validadas</option>
          <option value="TODAS" [selected]="filtro() === 'TODAS'">Todas</option>
        </select>
      </div>
      <button type="button" class="secundario" (click)="recargar()">Actualizar lista</button>
    </form>

    <div class="reparto" [class.con-detalle]="elegida()">
      <section class="panel" aria-labelledby="titulo-lista">
        <h2 id="titulo-lista">Evaluaciones ({{ filas().length }})</h2>
        @if (filas().length === 0) {
          <p class="vacio">{{ cargando() ? 'Consultando…' : 'No hay evaluaciones con este filtro.' }}</p>
        } @else {
          <div class="tabla-ancha" tabindex="0" role="region" aria-labelledby="titulo-lista">
            <table>
              <thead><tr><th>Activo</th><th>Municipio</th><th>Daño</th><th>Validación</th><th><span class="oculto">Acción</span></th></tr></thead>
              <tbody>
                @for (f of filasVisibles(); track f.idEvaluacion) {
                  <tr [class.elegida]="f.idEvaluacion === elegida()?.idEvaluacion">
                    <td>{{ f.tipo }}</td>
                    <td>{{ f.municipio }}</td>
                    <td class="nivel"><span class="muestra" [style.background]="color(f.nivel)"></span>{{ nombreNivel(f.nivel) }}</td>
                    <td>{{ validacion(f.estado) }}@if (f.conflicto) { <span class="conflicto">, con conflicto</span> }</td>
                    <td><button type="button" class="secundario" (click)="elegir(f)">Revisar</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (filas().length > filasVisibles().length) { <p class="suave">Se muestran las {{ filasVisibles().length }} de mayor daño.</p> }
        }
      </section>

      @if (elegida(); as f) {
        <section class="panel detalle" aria-labelledby="titulo-revision">
          <h2 id="titulo-revision">{{ f.tipo }}</h2>
          <p class="suave">{{ f.municipio }}. {{ nombreNivel(f.nivel) }}. {{ validacion(f.estado) }}. Dato al {{ f.datoAl | fechaHora }}</p>
          @if (f.conflicto) {
            <p class="aviso">Tiene un conflicto pendiente: resuélvalo en la bandeja antes de validarla.</p>
          }
          @if (errorAccion(); as e) { <p class="aviso error" role="alert">{{ e }}</p> }
          <div class="campo">
            <label for="val-motivo">Motivo <span class="suave">(obligatorio para rechazar)</span></label>
            <textarea id="val-motivo" rows="3" maxlength="2000" [value]="motivo()" (input)="motivo.set(valor($event))"></textarea>
          </div>
          <div class="acciones">
            <button type="button" [disabled]="enviando() || f.conflicto || f.estado === 'VALIDADA'" (click)="decidir(f, 'VALIDADA')">Validar</button>
            <button type="button" class="peligro" [disabled]="enviando() || f.conflicto" (click)="decidir(f, 'RECHAZADA')">Rechazar</button>
            @if (f.estado === 'VALIDADA') {
              <button type="button" class="secundario" [disabled]="enviando()" (click)="decidir(f, 'PENDIENTE')">Devolver a pendiente</button>
            }
            <button type="button" class="secundario" (click)="cerrar()">Cerrar</button>
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    h1 { margin-bottom: 1.25rem; }
    .aviso.degradado { border-color: var(--senal); }
    .mandos { display: grid; grid-template-columns: minmax(14rem, 2fr) minmax(10rem, 1fr) auto; gap: 0.9rem 1.25rem; align-items: end; margin-bottom: 1.25rem; }
    .mandos .campo { margin: 0; }
    .reparto.con-detalle { display: grid; grid-template-columns: minmax(0, 1fr) 22rem; gap: 1.25rem; align-items: start; }
    td button { padding: 0.2rem 0.55rem; font-size: 0.85rem; }
    tr.elegida td { background: var(--plano); }
    .nivel { white-space: nowrap; }
    .muestra { display: inline-block; width: 0.8rem; height: 0.8rem; margin-right: 0.4rem; border: 1px solid var(--tinta); vertical-align: -0.05rem; }
    .conflicto { color: var(--oxido); font-weight: 600; }
    .detalle { position: sticky; top: 1rem; }
    .detalle h2 { margin-bottom: 0.3rem; }
    .detalle > .suave { margin: 0 0 0.9rem; }
    .acciones { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .oculto { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
    @media (max-width: 62rem) { .reparto.con-detalle, .mandos { grid-template-columns: 1fr; } .detalle { position: static; } }
  `,
})
export class Validacion {
  private readonly mapa = inject(ApiMapa);
  private readonly api = inject(ApiEvaluaciones);
  private temporizador: ReturnType<typeof setTimeout> | undefined;
  esperaActualizacion = ESPERA_ACTUALIZACION_MS;

  readonly desastres = signal<FilaDesastre[]>([]);
  readonly idDesastre = signal('');
  readonly filtro = signal<FiltroEstado>('PENDIENTES');
  private readonly recargas = signal(0);
  private readonly datos = signal<MapaDanos | null>(null);
  /** Decisiones ya aceptadas que el modelo de lectura todavía no refleja. */
  private readonly decididas = signal<ReadonlyMap<string, EstadoValidacion>>(new Map());
  readonly cargando = signal(false);
  readonly degradada = signal(false);
  readonly errorLista = signal<string | null>(null);
  readonly confirmacion = signal<string | null>(null);
  readonly idElegida = signal<string | null>(null);
  readonly motivo = signal('');
  readonly enviando = signal(false);
  readonly errorAccion = signal<string | null>(null);

  private readonly todas = computed<FilaEvaluacion[]>(() => {
    const features = this.datos()?.features ?? [];
    const municipios = new Map(features.filter((f): f is ZonaDano => f.properties.tipo === 'zona').map((z) => [z.properties.codigoMunicipio, z.properties.nombre]));
    return features
      .filter((f): f is ActivoMapa => f.properties.tipo === 'activo')
      .map(({ properties: a }) => ({
        idEvaluacion: a.idEvaluacion,
        tipo: legible(a.tipoActivo),
        municipio: municipios.get(a.codigoMunicipio) ?? a.codigoMunicipio,
        nivel: a.nivelDano,
        peso: a.peso,
        estado: this.decididas().get(a.idEvaluacion) ?? a.estadoValidacion,
        conflicto: a.conflictoPendiente,
        datoAl: a.datoAl,
      }))
      .sort((a, b) => b.peso - a.peso);
  });

  readonly filas = computed(() =>
    this.todas().filter((f) => {
      switch (this.filtro()) {
        case 'VALIDADAS':
          return f.estado === 'VALIDADA';
        case 'PENDIENTES':
          return f.estado !== 'VALIDADA' && f.estado !== 'RECHAZADA';
        default:
          return true;
      }
    }),
  );
  readonly filasVisibles = computed(() => this.filas().slice(0, MAXIMO_FILAS));
  readonly elegida = computed(() => this.todas().find((f) => f.idEvaluacion === this.idElegida()) ?? null);

  readonly validacion = nombreValidacion;
  readonly nombreNivel = (nivel: NivelDano) => NOMBRE_NIVEL[nivel];
  readonly color = (nivel: NivelDano) => COLOR_NIVEL[nivel];

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.temporizador));
    this.mapa.desastres().subscribe({
      next: ({ cuerpo }) => {
        const orden = [...cuerpo.datos].sort(
          (a, b) => Number(a.estado === 'CERRADO') - Number(b.estado === 'CERRADO') || Date.parse(b.datoAl ?? '') - Date.parse(a.datoAl ?? ''),
        );
        this.desastres.set(orden);
        if (orden.length > 0) {
          this.idDesastre.set(orden[0].id);
        }
      },
      error: (e: HttpErrorResponse) => this.errorLista.set(leerError(e).mensaje),
    });

    effect((alLimpiar) => {
      const desastre = this.idDesastre();
      this.recargas();
      if (!desastre) return;
      this.cargando.set(true);
      this.errorLista.set(null);
      const lectura = this.mapa.danos({ desastre, nivelMinimo: 'SIN_DANO', soloValidadas: false }).subscribe({
        next: ({ cuerpo, degradado }) => {
          this.datos.set(cuerpo);
          this.degradada.set(degradado);
          this.cargando.set(false);
          this.olvidarLasYaReflejadas(cuerpo);
        },
        error: (e: HttpErrorResponse) => {
          this.errorLista.set(leerError(e).mensaje);
          this.cargando.set(false);
        },
      });
      alLimpiar(() => lectura.unsubscribe());
    });
  }

  private olvidarLasYaReflejadas(datos: MapaDanos): void {
    const actuales = new Map(datos.features.filter((f): f is ActivoMapa => f.properties.tipo === 'activo').map((a) => [a.properties.idEvaluacion, a.properties.estadoValidacion]));
    this.decididas.update((previas) => new Map([...previas].filter(([id, estado]) => actuales.has(id) && (actuales.get(id) ?? 'PENDIENTE') !== estado)));
  }

  valor(evento: Event): string {
    return (evento.target as HTMLInputElement).value;
  }

  elegirDesastre(id: string): void {
    if (id === this.idDesastre()) {
      return;
    }
    this.cerrar();
    this.confirmacion.set(null);
    this.decididas.set(new Map());
    this.datos.set(null);
    this.idDesastre.set(id);
  }

  recargar(): void {
    this.recargas.update((n) => n + 1);
  }

  elegir(fila: FilaEvaluacion): void {
    this.idElegida.set(fila.idEvaluacion);
    this.motivo.set('');
    this.errorAccion.set(null);
    this.confirmacion.set(null);
  }

  cerrar(): void {
    this.idElegida.set(null);
    this.errorAccion.set(null);
  }

  decidir(fila: FilaEvaluacion, estado: EstadoValidacion): void {
    const motivo = this.motivo().trim();
    if (estado === 'RECHAZADA' && !motivo) {
      this.errorAccion.set('Para rechazar una evaluación hay que indicar el motivo.');
      return;
    }
    this.enviando.set(true);
    this.errorAccion.set(null);
    this.api.validar(fila.idEvaluacion, estado, motivo).subscribe({
      next: () => {
        this.enviando.set(false);
        this.decididas.update((d) => new Map(d).set(fila.idEvaluacion, estado));
        const texto = { VALIDADA: 'validada', RECHAZADA: 'rechazada', PENDIENTE: 'devuelta a pendiente' }[estado];
        this.confirmacion.set(`${fila.tipo} de ${fila.municipio}: evaluación ${texto}.`);
        this.cerrar();
        clearTimeout(this.temporizador);
        this.temporizador = setTimeout(() => this.recargar(), this.esperaActualizacion);
      },
      error: (e: HttpErrorResponse) => {
        this.enviando.set(false);
        this.errorAccion.set(leerError(e).mensaje);
      },
    });
  }
}
