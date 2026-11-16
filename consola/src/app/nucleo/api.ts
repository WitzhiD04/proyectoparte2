import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { Candidata, Consolidado, Desastre, ErrorApi, Historial, Proyecto, Rol } from './modelos';
import { Sesion } from './sesion';

/** Agrega a cada petición el usuario, el rol y el ámbito del perfil actual. */
export const identidadInterceptor: HttpInterceptorFn = (peticion, siguiente) => {
  const perfil = inject(Sesion).perfil();
  let encabezados = peticion.headers.set('X-Usuario', perfil.usuario).set('X-Rol', perfil.rol);
  if (perfil.ambito) {
    encabezados = encabezados.set('X-Ambito', perfil.ambito);
  }
  return siguiente(peticion.clone({ headers: encabezados }));
};

/** Solo deja entrar a una pantalla a los roles indicados; a los demás los lleva a la lista de proyectos. */
export function soloRoles(...roles: Rol[]): CanActivateFn {
  return () => {
    const sesion = inject(Sesion);
    return roles.includes(sesion.perfil().rol) ? true : inject(Router).createUrlTree(['/proyectos']);
  };
}

/** Convierte una respuesta de error del servidor en algo que la pantalla puede mostrar. */
export function leerError(respuesta: HttpErrorResponse): ErrorApi {
  const cuerpo = respuesta.error as ErrorApi | null;
  if (cuerpo && typeof cuerpo === 'object' && cuerpo.mensaje) {
    return cuerpo;
  }
  return { codigo: 'SIN_CONEXION', mensaje: 'No se pudo completar la operación. Revise que el servicio esté en marcha e intente de nuevo.' };
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = '/v1';

  desastres(): Observable<Desastre[]> {
    return this.http.get<Desastre[]>(`${this.base}/desastres`);
  }

  candidatas(idDesastre: string): Observable<Candidata[]> {
    return this.http.get<Candidata[]>(`${this.base}/reconstruccion/candidatas`, { params: { desastre: idDesastre } });
  }

  crearLote(idDesastre: string, idsEvaluaciones: string[], claveIdempotencia: string): Observable<{ idsProyectos: string[]; reenvio: boolean }> {
    return this.http.post<{ idsProyectos: string[]; reenvio: boolean }>(`${this.base}/proyectos/lote`, { idDesastre, idsEvaluaciones, claveIdempotencia });
  }

  proyectos(idDesastre: string): Observable<Proyecto[]> {
    return this.http.get<Proyecto[]>(`${this.base}/proyectos`, { params: { desastre: idDesastre } });
  }

  proyecto(id: string): Observable<Proyecto> {
    return this.http.get<Proyecto>(`${this.base}/proyectos/${id}`);
  }

  asignarPresupuesto(id: string, cuerpo: { presupuesto: number | null; fuenteFinanciacion: string | null; inicio: string | null; fin: string | null; justificacion: string }): Observable<Proyecto> {
    return this.http.patch<Proyecto>(`${this.base}/proyectos/${id}/presupuesto`, cuerpo);
  }

  asignarResponsable(id: string, responsable: string, justificacion: string): Observable<Proyecto> {
    return this.http.patch<Proyecto>(`${this.base}/proyectos/${id}/responsable`, { responsable, justificacion });
  }

  transicion(id: string, accion: 'APROBAR' | 'INICIAR' | 'FINALIZAR' | 'CANCELAR', datos: { justificacion?: string; contratista?: string } = {}): Observable<Proyecto> {
    return this.http.post<Proyecto>(`${this.base}/proyectos/${id}/transiciones`, { accion, ...datos });
  }

  registrarAvance(id: string, cuerpo: { avance: number | null; costo: number | null; concepto: string | null; justificacion: string | null }): Observable<Proyecto> {
    return this.http.post<Proyecto>(`${this.base}/proyectos/${id}/avances`, cuerpo);
  }

  adjuntarDocumento(id: string, archivo: File): Observable<{ nombre: string; hash: string }> {
    const formulario = new FormData();
    formulario.append('archivo', archivo, archivo.name);
    return this.http.post<{ nombre: string; hash: string }>(`${this.base}/proyectos/${id}/documentos`, formulario);
  }

  historial(id: string, fecha?: string): Observable<Historial> {
    const params: Record<string, string> = fecha ? { fecha } : {};
    return this.http.get<Historial>(`${this.base}/proyectos/${id}/historial`, { params });
  }

  consolidado(idDesastre: string): Observable<Consolidado> {
    return this.http.get<Consolidado>(`${this.base}/programa/consolidado`, { params: { desastre: idDesastre } });
  }
}
