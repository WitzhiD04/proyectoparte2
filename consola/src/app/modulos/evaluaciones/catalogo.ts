import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { leerError } from '../../nucleo/api';
import { NOMBRE_NIVEL } from '../mapa/formato-mapa';
import { NIVELES, NivelDano } from '../mapa/modelos-mapa';
import { ApiEvaluaciones } from './api-evaluaciones';
import { TipoActivo } from './modelos-evaluaciones';

type Criterios = Record<NivelDano, string>;

const sinCriterios = (): Criterios => ({ SIN_DANO: '', LEVE: '', MODERADO: '', GRAVE: '', DESTRUCCION_TOTAL: '' });

/** Catálogo de tipos de activo (RF-ED-04, AD-28): cambia en ejecución y cada tipo define un criterio por nivel de daño. */
@Component({
  selector: 'app-catalogo',
  template: `
    <div class="cabeza">
      <h1>Tipos de activo</h1>
      <button type="button" (click)="nuevo()">Agregar tipo</button>
    </div>
    @if (errorLista(); as e) { <p class="aviso error" role="alert">{{ e }}</p> }
    @if (confirmacion(); as c) { <p class="aviso ok" role="status">{{ c }}</p> }

    @if (formularioAbierto()) {
      <section class="panel" aria-labelledby="titulo-tipo">
        <h2 id="titulo-tipo">{{ editando() ? 'Editar ' + editando() : 'Nuevo tipo de activo' }}</h2>
        <form (submit)="$event.preventDefault(); guardar()">
          <div class="fila-campos">
            @if (!editando()) {
              <div class="campo">
                <label for="tipo-codigo">Código</label>
                <input id="tipo-codigo" maxlength="40" [value]="codigo()" (input)="codigo.set(valor($event))" aria-describedby="ayuda-codigo" />
                <p id="ayuda-codigo" class="suave ayuda">Minúsculas, números y guion bajo; empieza por una letra.</p>
              </div>
            }
            <div class="campo">
              <label for="tipo-nombre">Nombre</label>
              <input id="tipo-nombre" maxlength="100" [value]="nombre()" (input)="nombre.set(valor($event))" />
            </div>
          </div>
          <fieldset>
            <legend>Criterio de cada nivel de daño</legend>
            @for (n of niveles; track n) {
              <div class="campo">
                <label [attr.for]="'criterio-' + n">{{ nombreNivel(n) }}</label>
                <textarea [id]="'criterio-' + n" rows="2" maxlength="300" [value]="criterios()[n]" (input)="cambiarCriterio(n, valor($event))"></textarea>
              </div>
            }
          </fieldset>
          @if (errorForm(); as e) { <p class="aviso error" role="alert">{{ e }}</p> }
          <div class="acciones">
            <button type="submit" [disabled]="enviando()">Guardar</button>
            <button type="button" class="secundario" (click)="cerrar()">Cancelar</button>
          </div>
        </form>
      </section>
    }

    <section class="panel" aria-labelledby="titulo-tipos">
      <h2 id="titulo-tipos">Catálogo ({{ tipos().length }})</h2>
      @if (tipos().length === 0) {
        <p class="vacio">{{ cargando() ? 'Consultando…' : 'El catálogo está vacío.' }}</p>
      } @else {
        <div class="tabla-ancha" tabindex="0" role="region" aria-labelledby="titulo-tipos">
          <table>
            <thead><tr><th>Código</th><th>Nombre</th><th class="numero">Versión</th><th><span class="oculto">Acción</span></th></tr></thead>
            <tbody>
              @for (t of tipos(); track t.codigo) {
                <tr>
                  <td>{{ t.codigo }}</td>
                  <td>{{ t.nombre }}</td>
                  <td class="numero">{{ t.version ?? 1 }}</td>
                  <td><button type="button" class="secundario" [attr.aria-label]="'Editar ' + t.nombre" (click)="editar(t)">Editar</button></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: `
    .cabeza { display: flex; flex-wrap: wrap; gap: 0.75rem 1rem; align-items: center; justify-content: space-between; margin-bottom: 1.25rem; }
    fieldset { border: 0; padding: 0; margin: 0; }
    legend { font-family: var(--titulo); font-stretch: 75%; font-weight: 700; font-size: 1.15rem; padding: 0; margin-bottom: 0.5rem; }
    .ayuda { margin: 0.2rem 0 0; font-size: 0.85rem; }
    .acciones { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    td button { padding: 0.2rem 0.55rem; font-size: 0.85rem; }
    .oculto { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
  `,
})
export class Catalogo {
  private readonly api = inject(ApiEvaluaciones);

  readonly niveles = NIVELES;
  readonly nombreNivel = (nivel: NivelDano) => NOMBRE_NIVEL[nivel];
  readonly tipos = signal<TipoActivo[]>([]);
  readonly cargando = signal(true);
  readonly errorLista = signal<string | null>(null);
  readonly confirmacion = signal<string | null>(null);
  readonly formularioAbierto = signal(false);
  readonly editando = signal<string | null>(null);
  readonly codigo = signal('');
  readonly nombre = signal('');
  readonly criterios = signal<Criterios>(sinCriterios());
  readonly enviando = signal(false);
  readonly errorForm = signal<string | null>(null);

  constructor() {
    this.cargar();
  }

  private cargar(): void {
    this.api.tiposActivo().subscribe({
      next: (lista) => {
        this.tipos.set(lista);
        this.cargando.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.errorLista.set(leerError(e).mensaje);
        this.cargando.set(false);
      },
    });
  }

  valor(evento: Event): string {
    return (evento.target as HTMLInputElement).value;
  }

  cambiarCriterio(nivel: NivelDano, texto: string): void {
    this.criterios.update((c) => ({ ...c, [nivel]: texto }));
  }

  private abrir(editando: string | null, nombre: string, criterios: Criterios): void {
    this.editando.set(editando);
    this.codigo.set('');
    this.nombre.set(nombre);
    this.criterios.set(criterios);
    this.errorForm.set(null);
    this.confirmacion.set(null);
    this.formularioAbierto.set(true);
  }

  nuevo(): void {
    this.abrir(null, '', sinCriterios());
  }

  editar(tipo: TipoActivo): void {
    const criterios = sinCriterios();
    for (const c of tipo.criterios) {
      if (c.nivel in criterios) {
        criterios[c.nivel as NivelDano] = c.criterio;
      }
    }
    this.abrir(tipo.codigo, tipo.nombre, criterios);
  }

  cerrar(): void {
    this.formularioAbierto.set(false);
  }

  guardar(): void {
    const editando = this.editando();
    const nombre = this.nombre().trim();
    const criterios = NIVELES.map((nivel) => ({ nivel, criterio: this.criterios()[nivel].trim() }));
    const peticion = editando
      ? this.api.modificarTipo(editando, nombre, criterios)
      : this.api.crearTipo(this.codigo().trim(), nombre, criterios);
    this.enviando.set(true);
    this.errorForm.set(null);
    peticion.subscribe({
      next: (tipo) => {
        this.enviando.set(false);
        this.tipos.update((lista) => [...lista.filter((t) => t.codigo !== tipo.codigo), tipo].sort((a, b) => a.codigo.localeCompare(b.codigo)));
        this.confirmacion.set(`Tipo de activo ${tipo.nombre} guardado.`);
        this.cerrar();
      },
      error: (e: HttpErrorResponse) => {
        this.enviando.set(false);
        this.errorForm.set(leerError(e).mensaje);
      },
    });
  }
}
