import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Conflicto, CriterioTipo, EstadoConflicto, EstadoValidacion, SolicitudResolucion, TipoActivo } from './modelos-evaluaciones';

/** Revisión humana en Evaluación de Daños: validación, bandeja de conflictos y catálogo de tipos de activo. */
@Injectable({ providedIn: 'root' })
export class ApiEvaluaciones {
  private readonly http = inject(HttpClient);

  validar(idEvaluacion: string, estado: EstadoValidacion, motivo: string): Observable<unknown> {
    return this.http.post(`/v1/evaluaciones/${idEvaluacion}/validacion`, { estado, motivo: motivo || null });
  }

  conflictos(estado: EstadoConflicto): Observable<Conflicto[]> {
    return this.http.get<Conflicto[]>('/v1/conflictos', { params: { estado } });
  }

  resolver(idConflicto: string, solicitud: SolicitudResolucion): Observable<unknown> {
    return this.http.post(`/v1/conflictos/${idConflicto}/resolucion`, solicitud);
  }

  tiposActivo(): Observable<TipoActivo[]> {
    return this.http.get<TipoActivo[]>('/v1/catalogos/tipos-activo');
  }

  crearTipo(codigo: string, nombre: string, criterios: CriterioTipo[]): Observable<TipoActivo> {
    return this.http.post<TipoActivo>('/v1/catalogos/tipos-activo', { codigo, nombre, criterios });
  }

  modificarTipo(codigo: string, nombre: string, criterios: CriterioTipo[]): Observable<TipoActivo> {
    return this.http.patch<TipoActivo>(`/v1/catalogos/tipos-activo/${encodeURIComponent(codigo)}`, { nombre, criterios });
  }
}
