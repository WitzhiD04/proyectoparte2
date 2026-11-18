import { Component, inject, input } from '@angular/core';
import { Sesion } from '../../nucleo/sesion';

/** Pantalla pública: lleva a Keycloak, y es donde se vuelve al cerrar sesión o si la sesión vence. */
@Component({
  selector: 'app-ingresar',
  template: `
    <section class="panel ingreso">
      <h1>Ingreso a la plataforma</h1>
      <p>Inicie sesión con su usuario de la plataforma. Verá las secciones que corresponden a su rol.</p>
      @if (sesion.aviso(); as aviso) { <p class="aviso error" role="alert">{{ aviso }}</p> }
      <button type="button" (click)="sesion.ingresar(volver())">Iniciar sesión</button>
    </section>
  `,
  styles: `
    .ingreso { max-width: 32rem; margin: 3rem auto 0; }
    h1 { margin-bottom: 0.8rem; }
    p { margin: 0 0 1.2rem; }
  `,
})
export class Ingresar {
  /** A dónde volver después de iniciar sesión (parámetro ?volver=). */
  readonly volver = input<string>();
  protected readonly sesion = inject(Sesion);
}
