import { Injectable, computed, signal } from '@angular/core';
import { Perfil } from './modelos';

/**
 * Quién usa la consola. El inicio de sesión real es del API Gateway de la plataforma, que todavía no
 * existe; mientras tanto se elige un perfil de prueba y la consola lo envía en los encabezados.
 */
@Injectable({ providedIn: 'root' })
export class Sesion {
  readonly perfiles: Perfil[] = [
    { usuario: 'alcaldia.armenia', nombre: 'Autoridad local (Alcaldía de Armenia)', rol: 'Autoridad', ambito: 'Local' },
    { usuario: 'gobernacion.quindio', nombre: 'Autoridad regional (Gobernación del Quindío)', rol: 'Autoridad', ambito: 'Regional' },
    { usuario: 'ing.perez', nombre: 'Responsable (Ing. Pérez)', rol: 'Responsable' },
    { usuario: 'ing.gomez', nombre: 'Responsable (Ing. Gómez)', rol: 'Responsable' },
    { usuario: 'contraloria', nombre: 'Auditor (Contraloría)', rol: 'Auditor' },
  ];

  readonly perfil = signal<Perfil>(this.perfilGuardado());
  readonly idDesastre = signal<string>('');

  readonly esAutoridad = computed(() => this.perfil().rol === 'Autoridad');
  readonly esResponsable = computed(() => this.perfil().rol === 'Responsable');
  readonly esAuditor = computed(() => this.perfil().rol === 'Auditor');

  elegirPerfil(usuario: string): void {
    const perfil = this.perfiles.find((p) => p.usuario === usuario);
    if (perfil) {
      this.perfil.set(perfil);
      try {
        localStorage.setItem('perfil', perfil.usuario);
      } catch {
        // sin almacenamiento: el perfil dura hasta recargar la página
      }
    }
  }

  /** Recuerda el perfil elegido para que no se pierda al recargar. */
  private perfilGuardado(): Perfil {
    let usuario: string | null = null;
    try {
      usuario = localStorage.getItem('perfil');
    } catch {
      usuario = null;
    }
    return this.perfiles.find((p) => p.usuario === usuario) ?? this.perfiles[0];
  }
}
