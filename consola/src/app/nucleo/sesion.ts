import { Injectable, computed, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { EventTypes, OidcSecurityService, PublicEventsService } from 'angular-auth-oidc-client';
import { Observable, catchError, filter, finalize, firstValueFrom, map, of, shareReplay, switchMap, take, tap, throwError } from 'rxjs';
import { RUTA_INGRESO } from './autenticacion/configuracion-oidc';
import { Rol } from './modelos';

/** Roles del realm "plataforma" (DA-15). Los demás roles del token (los de Keycloak) no le interesan a la consola. */
export const ROLES_REALM = ['autoridad', 'responsable-proyecto', 'auditor', 'evaluador', 'operador', 'organismo', 'administrador'] as const;
export type RolRealm = (typeof ROLES_REALM)[number];

export interface Identidad {
  usuario: string;
  nombre: string;
  roles: RolRealm[];
  organizacion: string | null;
  ambito: 'LOCAL' | 'REGIONAL' | 'NACIONAL' | null;
  territorio: string | null;
  /** Vencimiento del token, en segundos desde 1970. */
  vence: number;
}

const CLAVE_DESTINO = 'consola.destino';
/** Un token que vence en menos de esto se renueva antes de usarlo. */
const MARGEN_VENCIMIENTO_SEGUNDOS = 30;

/** Lee del token lo que usa la consola: los mismos claims que valida el gateway. */
export function identidadDesdeToken(token: Record<string, unknown> | null | undefined): Identidad | null {
  const usuario = token?.['preferred_username'];
  if (typeof usuario !== 'string' || !usuario) {
    return null;
  }
  const acceso = token?.['realm_access'] as { roles?: unknown } | undefined;
  const delToken = Array.isArray(acceso?.roles) ? acceso.roles : [];
  const ambito = texto(token?.['ambito'])?.toUpperCase();
  return {
    usuario,
    nombre: texto(token?.['name']) ?? usuario,
    roles: ROLES_REALM.filter((rol) => delToken.includes(rol)),
    organizacion: texto(token?.['organizacion']),
    ambito: ambito === 'LOCAL' || ambito === 'REGIONAL' || ambito === 'NACIONAL' ? ambito : null,
    territorio: texto(token?.['territorio']),
    vence: typeof token?.['exp'] === 'number' ? token['exp'] : 0,
  };
}

function texto(valor: unknown): string | null {
  return typeof valor === 'string' && valor.trim() ? valor.trim() : null;
}

/** Solo rutas internas: un destino externo convertiría el inicio de sesión en una redirección abierta. */
export function destinoSeguro(destino: string | null | undefined): string | null {
  if (!destino || !destino.startsWith('/') || destino.startsWith('//') || destino.startsWith('/\\') || destino.startsWith(RUTA_INGRESO)) {
    return null;
  }
  return destino;
}

/** Quién usa la consola: sale del token de Keycloak. Ya no hay perfiles de prueba. */
@Injectable({ providedIn: 'root' })
export class Sesion {
  private readonly oidc = inject(OidcSecurityService);
  private readonly eventos = inject(PublicEventsService);
  private readonly router = inject(Router);
  private renovacion: Observable<string> | null = null;

  readonly identidad = signal<Identidad | null>(null);
  /** Mensaje para la pantalla de inicio de sesión (sesión vencida, Keycloak sin respuesta...). */
  readonly aviso = signal<string | null>(null);
  readonly idDesastre = signal<string>('');

  readonly autenticada = computed(() => this.identidad() !== null);
  readonly usuario = computed(() => this.identidad()?.usuario ?? '');
  readonly roles = computed<readonly RolRealm[]>(() => this.identidad()?.roles ?? []);

  /** Rol en Reconstrucción, con la misma prioridad que aplica el servicio si alguien tiene varios. */
  readonly rolReconstruccion = computed<Rol | null>(() => {
    const roles = this.roles();
    if (roles.includes('autoridad')) return 'Autoridad';
    if (roles.includes('responsable-proyecto')) return 'Responsable';
    if (roles.includes('auditor')) return 'Auditor';
    return null;
  });
  readonly esAutoridad = computed(() => this.rolReconstruccion() === 'Autoridad');
  readonly esResponsable = computed(() => this.rolReconstruccion() === 'Responsable');
  readonly esAuditor = computed(() => this.rolReconstruccion() === 'Auditor');

  constructor() {
    // La librería renueva el token por su cuenta: la identidad se mantiene al día con cada token nuevo.
    this.eventos.registerForEvents()
      .pipe(filter((evento) => evento.type === EventTypes.NewAuthenticationResult), switchMap(() => this.leerIdentidad()))
      .subscribe();
  }

  /** Al arrancar: procesa la respuesta de Keycloak, si la hay, o recupera la sesión guardada. Nunca impide arrancar. */
  iniciar(): Promise<void> {
    const volvioDeKeycloak = /[?&](code|error)=/.test(window.location.search);
    return firstValueFrom(this.oidc.checkAuth().pipe(
      switchMap((resultado) => {
        if (resultado.isAuthenticated) {
          return this.leerIdentidad();
        }
        if (volvioDeKeycloak) {
          this.aviso.set('No se pudo completar el inicio de sesión. Intente de nuevo.');
        }
        return of(null);
      }),
      catchError(() => {
        this.aviso.set('El servicio de identidad no responde. Intente de nuevo en un momento.');
        return of(null);
      }),
      map(() => undefined),
    ));
  }

  /** Lleva a la pantalla de Keycloak y recuerda a dónde volver. */
  ingresar(destino?: string | null): void {
    this.aviso.set(null);
    const seguro = destinoSeguro(destino);
    if (seguro) {
      sessionStorage.setItem(CLAVE_DESTINO, seguro);
    }
    this.oidc.preloadAuthWellKnownDocument().subscribe({
      next: () => this.oidc.authorize(),
      error: () => this.aviso.set('El servicio de identidad no responde. Intente de nuevo en un momento.'),
    });
  }

  /** A dónde ir después de iniciar sesión. Se usa una sola vez. */
  tomarDestino(): string | null {
    const destino = destinoSeguro(sessionStorage.getItem(CLAVE_DESTINO));
    sessionStorage.removeItem(CLAVE_DESTINO);
    return destino;
  }

  salir(): void {
    sessionStorage.removeItem(CLAVE_DESTINO);
    this.oidc.logoff().subscribe();
  }

  /** El token para una petición: si está por vencer, primero lo renueva. */
  tokenVigente(): Observable<string> {
    return this.oidc.getAccessToken().pipe(take(1), switchMap((token) => {
      if (!token) {
        return throwError(() => new Error('Sin sesión'));
      }
      // Sin seguimiento: las pantallas piden datos dentro de un effect y no deben repetir la petición al renovar el token.
      const vence = untracked(this.identidad)?.vence ?? 0;
      return vence - Date.now() / 1000 < MARGEN_VENCIMIENTO_SEGUNDOS ? this.renovar() : of(token);
    }));
  }

  /** Pide un token nuevo. Las peticiones simultáneas comparten la misma renovación. */
  renovar(): Observable<string> {
    // Un solo resultado: si la librería emitiera dos, el interceptor repetiría la petición dos veces.
    this.renovacion ??= this.oidc.forceRefreshSession().pipe(
      take(1),
      switchMap((resultado) => {
        if (!resultado?.isAuthenticated || !resultado.accessToken) {
          return throwError(() => new Error('No se pudo renovar la sesión'));
        }
        return this.leerIdentidad().pipe(map(() => resultado.accessToken));
      }),
      finalize(() => (this.renovacion = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.renovacion;
  }

  /** El token ya no sirve y no se pudo renovar: se vuelve a la pantalla de inicio de sesión. */
  sesionVencida(): void {
    const destino = destinoSeguro(this.router.url);
    this.oidc.logoffLocal();
    this.identidad.set(null);
    this.aviso.set('Su sesión venció. Inicie sesión de nuevo para continuar.');
    void this.router.navigate([RUTA_INGRESO], { queryParams: destino ? { volver: destino } : {} });
  }

  private leerIdentidad(): Observable<Identidad | null> {
    return this.oidc.getPayloadFromAccessToken().pipe(
      take(1),
      map((token) => identidadDesdeToken(token)),
      tap((identidad) => this.identidad.set(identidad)),
    );
  }
}
