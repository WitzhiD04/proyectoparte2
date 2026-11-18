import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { Api, leerError } from './nucleo/api';
import { menuPara, moduloDe } from './nucleo/menu';
import { Desastre, ErrorApi } from './nucleo/modelos';
import { Sesion } from './nucleo/sesion';

const NOMBRE_AMBITO = { LOCAL: 'local', REGIONAL: 'regional', NACIONAL: 'nacional' } as const;
const NOMBRE_ROL: Record<string, string> = {
  autoridad: 'Autoridad', 'responsable-proyecto': 'Responsable de proyecto', auditor: 'Auditor', organismo: 'Organismo de respuesta',
  operador: 'Operador', administrador: 'Administrador', evaluador: 'Evaluador',
};

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly sesion = inject(Sesion);
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map((e) => e.urlAfterRedirects)),
    { initialValue: this.router.url },
  );

  protected readonly menu = computed(() => menuPara(this.sesion.roles()));
  protected readonly modulo = computed(() => moduloDe(this.url()) ?? 'Consola de coordinación');
  protected readonly enReconstruccion = computed(() => this.sesion.autenticada() && moduloDe(this.url()) === 'Reconstrucción');
  /** Lo que se muestra junto al usuario: su rol y, si es una autoridad, su ámbito. */
  protected readonly descripcionUsuario = computed(() => {
    const identidad = this.sesion.identidad();
    if (!identidad) {
      return '';
    }
    const rol = identidad.roles.map((r) => NOMBRE_ROL[r]).join(', ');
    return identidad.ambito && identidad.roles.includes('autoridad') ? `${rol} ${NOMBRE_AMBITO[identidad.ambito]}` : rol;
  });

  protected readonly desastres = signal<Desastre[]>([]);
  protected readonly desastresCargados = signal(false);
  protected readonly errorDesastres = signal<ErrorApi | null>(null);

  constructor() {
    // Los desastres solo hacen falta en Reconstrucción, y solo quien opera allí puede pedirlos.
    effect(() => {
      if (this.enReconstruccion() && !untracked(this.desastresCargados)) {
        untracked(() => this.cargarDesastres());
      }
    });
  }

  private cargarDesastres(): void {
    this.desastresCargados.set(true);
    this.api.desastres().subscribe({
      next: (lista) => {
        this.errorDesastres.set(null);
        this.desastres.set(lista);
        const enReconstruccion = lista.find((d) => d.enReconstruccion) ?? lista[0];
        if (enReconstruccion && !this.sesion.idDesastre()) {
          this.sesion.idDesastre.set(enReconstruccion.id);
        }
      },
      error: (r) => this.errorDesastres.set(leerError(r)),
    });
  }

  protected cambiarDesastre(id: string): void {
    this.sesion.idDesastre.set(id);
    this.router.navigate(['/reconstruccion/proyectos']);
  }
}
