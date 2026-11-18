import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Candidata, Consolidado, Desastre, ErrorApi, Historial, Proyecto } from './modelos';

/**
 * Convierte una respuesta de error en algo que la pantalla puede mostrar. El mensaje es el del servicio: ante un 403 la
 * consola no esconde la regla que se incumplió.
 */
export function leerError(respuesta: HttpErrorResponse): ErrorApi {
  const cuerpo = respuesta.error as (ErrorApi & { error?: string }) | null;
  if (cuerpo && typeof cuerpo === 'object' && cuerpo.mensaje) {
    return cuerpo;
  }
  // Evaluación de Daños responde { error: "..." }.
  if (cuerpo && typeof cuerpo === 'object' && cuerpo.error) {
    return { codigo: `HTTP_${respuesta.status}`, mensaje: cuerpo.error };
  }
  if (respuesta.status === 403) {
    return { codigo: 'NO_AUTORIZADO', mensaje: 'Su usuario no tiene permiso para esta operación.' };
  }
  return { codigo: 'SIN_CONEXION', mensaje: 'No se pudo completar la operación. Revise que el servicio esté en marcha e intente de nuevo.' };
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = '/v1';

  desastres(): Observable<Desastre[]> {
    return this.http.get<Desastre[]>(`${this.base}/reconstruccion/desastres`);
  }

  candidatas(idDesastre: string): Observable<Candidata[]> {
    return this.http.get<Candidata[]>(`${this.base}/reconstruccion/candidatas`, { params: { desastre: idDesastre } });
  }

  crearLote(idDesastre: string, idsEvaluaciones: string[], claveIdempotencia: string): Observable<{ idsProyectos: string[]; reenvio: boolean }> {
    return this.http.post<{ idsProyectos: string[]; reenvio: boolean }>(`${this.base}/proyectos:lote`, { idDesastre, idsEvaluaciones, claveIdempotencia });
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
    return this.http.post<Proyecto>(`${this.base}/proyectos/${id}/avance`, cuerpo);
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
