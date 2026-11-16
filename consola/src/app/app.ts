import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api } from './nucleo/api';
import { Desastre } from './nucleo/modelos';
import { Sesion } from './nucleo/sesion';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  protected readonly sesion = inject(Sesion);
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly desastres = signal<Desastre[]>([]);
  protected readonly sinServicio = signal(false);

  ngOnInit(): void {
    this.api.desastres().subscribe({
      next: (lista) => {
        this.desastres.set(lista);
        const enReconstruccion = lista.find((d) => d.enReconstruccion) ?? lista[0];
        if (enReconstruccion && !this.sesion.idDesastre()) {
          this.sesion.idDesastre.set(enReconstruccion.id);
        }
      },
      error: () => this.sinServicio.set(true),
    });
  }

  protected cambiarPerfil(usuario: string): void {
    this.sesion.elegirPerfil(usuario);
    // Cada rol ve pantallas distintas: al cambiar se vuelve a la lista, que todos pueden ver.
    this.router.navigateByUrl('/', { skipLocationChange: true }).then(() => this.router.navigate(['/proyectos']));
  }

  protected cambiarDesastre(id: string): void {
    this.sesion.idDesastre.set(id);
    this.router.navigate(['/proyectos']);
  }
}
