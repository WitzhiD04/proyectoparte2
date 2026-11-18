import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { EvolucionActivo, EvolucionDesastre, EvolucionZona, FiltroDanos, MapaDanos, MapaZonas, Respuesta, TableroDesastres } from './modelos-mapa';

/** Lecturas del mapa: Consulta de Daños (coropleta y activos) y Consulta (desastres y sus zonas). */
@Injectable({ providedIn: 'root' })
export class ApiMapa {
  private readonly http = inject(HttpClient);

  private leer<T extends object>(ruta: string, params = new HttpParams()): Observable<Respuesta<T>> {
    return this.http.get<T>(`/v1/${ruta}`, { params, observe: 'response' }).pipe(
      map((r) => ({
        cuerpo: r.body as T,
        degradado: (r.body as { degradado?: boolean } | null)?.degradado === true || r.headers.get('X-Degradado') === 'true',
      })),
    );
  }

  desastres(): Observable<Respuesta<TableroDesastres>> {
    return this.leer('consulta/desastres');
  }

  /** Siempre pide los puntos: encender o apagar la capa de activos no vuelve a consultar. */
  danos(filtro: FiltroDanos): Observable<Respuesta<MapaDanos>> {
    const params = new HttpParams({
      fromObject: { desastre: filtro.desastre, nivelMinimo: filtro.nivelMinimo, soloValidadas: filtro.soloValidadas, puntos: true },
    });
    return this.leer('consulta-danos/mapa', params);
  }

  /** Incluye los desastres cerrados: también se pueden elegir en el mapa. */
  zonas(): Observable<Respuesta<MapaZonas>> {
    return this.leer('consulta/mapa', new HttpParams({ fromObject: { soloActivos: false } }));
  }

  evolucionDesastre(id: string, en?: string): Observable<Respuesta<EvolucionDesastre>> {
    return this.leer(`consulta/desastres/${id}/evolucion`, en ? new HttpParams({ fromObject: { en } }) : undefined);
  }

  evolucionZona(codigo: string, desastre: string): Observable<Respuesta<EvolucionZona>> {
    return this.leer(`consulta-danos/zonas/${codigo}/evolucion`, new HttpParams({ fromObject: { desastre } }));
  }

  evolucionActivo(id: string): Observable<Respuesta<EvolucionActivo>> {
    return this.leer(`consulta-danos/activos/${id}/evolucion`);
  }
}
