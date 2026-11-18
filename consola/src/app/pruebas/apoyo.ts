import { Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EventTypes, OidcSecurityService, PublicEventsService } from 'angular-auth-oidc-client';
import { Observable, Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { RolRealm, Sesion } from '../nucleo/sesion';
import { LienzoMapa } from '../modulos/mapa/lienzo-mapa';
import { ActivoMapa, NombreCapa, ZonaDano, ZonaDibujo } from '../modulos/mapa/modelos-mapa';

/** Claims de un token como los de Keycloak, vigente por una hora. */
export function tokenDe(usuario: string, roles: RolRealm[], extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    preferred_username: usuario,
    name: usuario,
    realm_access: { roles: ['default-roles-plataforma', 'offline_access', ...roles] },
    exp: Math.floor(Date.now() / 1000) + 3600,
    ...extra,
  };
}

/** Sustituye a la librería OIDC: no hay Keycloak en las pruebas de componentes. */
export class OidcFalso {
  token = 'token-1';
  claims: Record<string, unknown> | null = null;
  resultadoInicio: Observable<unknown> | null = null;

  checkAuth = vi.fn(() => this.resultadoInicio ?? of({ isAuthenticated: this.claims !== null, accessToken: this.token }));
  getAccessToken = vi.fn(() => of(this.token));
  getPayloadFromAccessToken = vi.fn(() => of(this.claims));
  forceRefreshSession = vi.fn((): Observable<unknown> => {
    this.token = 'token-2';
    return of({ isAuthenticated: true, accessToken: this.token });
  });
  preloadAuthWellKnownDocument = vi.fn((): Observable<unknown> => of({}));
  authorize = vi.fn();
  logoff = vi.fn(() => of(null));
  logoffLocal = vi.fn();
}

export class EventosFalsos {
  readonly eventos = new Subject<{ type: EventTypes; value: unknown }>();
  registerForEvents = () => this.eventos.asObservable();
}

export function proveedoresOidc(oidc: OidcFalso, eventos = new EventosFalsos()): Provider[] {
  return [
    { provide: OidcSecurityService, useValue: oidc },
    { provide: PublicEventsService, useValue: eventos },
  ];
}

/** Deja la sesión iniciada con los claims indicados. */
export async function iniciarSesionComo(oidc: OidcFalso, claims: Record<string, unknown>): Promise<Sesion> {
  oidc.claims = claims;
  const sesion = TestBed.inject(Sesion);
  await sesion.iniciar();
  return sesion;
}

/** Sustituye a Leaflet: jsdom no tiene canvas ni tamaños, y el mapa real se comprueba en el navegador. */
export class LienzoFalso implements LienzoMapa {
  alElegir: (id: string) => void = () => undefined;
  alElegirZona: (codigo: string) => void = () => undefined;
  coropleta: readonly ZonaDano[] = [];
  activos: readonly ActivoMapa[] = [];
  zona: ZonaDibujo | null = null;
  anteriores: readonly ZonaDibujo[] = [];
  marcado: string | null = null;
  visibles: Partial<Record<NombreCapa, boolean>> = {};
  encuadres = 0;
  destruido = false;

  montar(_elemento: HTMLElement, alElegirActivo: (id: string) => void, alElegirZona: (codigo: string) => void): void {
    this.alElegir = alElegirActivo;
    this.alElegirZona = alElegirZona;
  }
  pintarCoropleta(zonas: readonly ZonaDano[]): void { this.coropleta = zonas; }
  pintarActivos(activos: readonly ActivoMapa[]): void { this.activos = activos; }
  pintarZona(vigente: ZonaDibujo | null, anteriores: readonly ZonaDibujo[]): void { this.zona = vigente; this.anteriores = anteriores; }
  marcarActivo(id: string | null): void { this.marcado = id; }
  mostrar(capa: NombreCapa, visible: boolean): void { this.visibles[capa] = visible; }
  encuadrar(): void { this.encuadres++; }
  destruir(): void { this.destruido = true; }
}
