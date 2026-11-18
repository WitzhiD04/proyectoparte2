import { InjectionToken } from '@angular/core';
import type { GeoJsonObject } from 'geojson';
import * as L from 'leaflet';
import { COLOR_NIVEL, NOMBRE_NIVEL, colorIndice, nombreValidacion, numero, legible, radioNivel } from './formato-mapa';
import { ActivoMapa, NIVELES, NombreCapa, ZonaDano, ZonaDibujo } from './modelos-mapa';

/** Lo que la pantalla necesita del mapa. Leaflet queda detrás de esta interfaz. */
export interface LienzoMapa {
  montar(elemento: HTMLElement, alElegirActivo: (idActivo: string) => void): void;
  pintarCoropleta(zonas: readonly ZonaDano[]): void;
  pintarActivos(activos: readonly ActivoMapa[]): void;
  pintarZona(vigente: ZonaDibujo | null, anteriores: readonly ZonaDibujo[]): void;
  marcarActivo(idActivo: string | null): void;
  mostrar(capa: NombreCapa, visible: boolean): void;
  encuadrar(): void;
  destruir(): void;
}

export const FABRICA_LIENZO = new InjectionToken<() => LienzoMapa>('Fábrica del lienzo del mapa', {
  providedIn: 'root',
  factory: () => () => new LienzoLeaflet(),
});

const TINTA = '#12313f';
const PAPEL = '#ffffff';
const SENAL = '#f2b705';
const COLOMBIA: L.LatLngTuple = [4.57, -74.3];

function globo(titulo: string, lineas: readonly string[]): HTMLElement {
  // Con textContent: los nombres vienen de otros sistemas y no se interpretan como HTML.
  const caja = document.createElement('div');
  const encabezado = document.createElement('strong');
  encabezado.textContent = titulo;
  caja.append(encabezado);
  for (const linea of lineas) {
    const fila = document.createElement('div');
    fila.textContent = linea;
    caja.append(fila);
  }
  return caja;
}

function globoZona({ properties: z }: ZonaDano): HTMLElement {
  const niveles = NIVELES.filter((n) => z.distribucion[n] > 0).map((n) => `${NOMBRE_NIVEL[n]}: ${z.distribucion[n]}`);
  const conflictos = z.conConflicto > 0 ? [`Con conflicto pendiente: ${z.conConflicto}`] : [];
  const indice = z.indiceDano === null ? 'sin dato' : numero(z.indiceDano);
  return globo(z.nombre ?? z.codigoMunicipio, [`Índice de daño: ${indice}`, `Activos: ${z.activos}`, ...niveles, ...conflictos]);
}

function globoActivo({ properties: a }: ActivoMapa): HTMLElement {
  const conflicto = a.conflictoPendiente ? ['Conflicto pendiente'] : [];
  return globo(legible(a.tipoActivo), [NOMBRE_NIVEL[a.nivelDano], nombreValidacion(a.estadoValidacion), ...conflicto, 'Clic para ver su evolución']);
}

/**
 * Mapa con Leaflet. Todo se dibuja en un solo canvas: aguanta miles de puntos y la capa de arriba no le quita los eventos
 * a la de abajo. El orden es siempre coropleta, zona del desastre y activos.
 */
export class LienzoLeaflet implements LienzoMapa {
  private mapa: L.Map | null = null;
  private observador: ResizeObserver | null = null;
  private alElegirActivo: (idActivo: string) => void = () => undefined;
  private readonly marcadores = new Map<string, L.CircleMarker>();
  private anillo: L.CircleMarker | null = null;
  private elegido: string | null = null;
  private readonly capas: Record<NombreCapa, L.FeatureGroup> = {
    coropleta: L.featureGroup(),
    zona: L.featureGroup(),
    activos: L.featureGroup(),
  };

  montar(elemento: HTMLElement, alElegirActivo: (idActivo: string) => void): void {
    this.alElegirActivo = alElegirActivo;
    const mapa = L.map(elemento, { renderer: L.canvas({ padding: 0.3, tolerance: 4 }), zoomControl: false }).setView(COLOMBIA, 6);
    L.control.zoom({ zoomInTitle: 'Acercar', zoomOutTitle: 'Alejar' }).addTo(mapa);
    mapa.attributionControl.setPrefix('<a href="https://leafletjs.com">Leaflet</a>');
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
    }).addTo(mapa);
    for (const capa of Object.values(this.capas)) {
      capa.addTo(mapa);
    }
    this.mapa = mapa;
    this.observador = new ResizeObserver(() => mapa.invalidateSize());
    this.observador.observe(elemento);
  }

  pintarCoropleta(zonas: readonly ZonaDano[]): void {
    this.capas.coropleta.clearLayers();
    for (const zona of zonas) {
      if (!zona.geometry) {
        continue;
      }
      L.geoJSON(zona as unknown as GeoJsonObject, {
        style: { color: PAPEL, weight: 1.5, fillColor: colorIndice(zona.properties.indiceDano), fillOpacity: 0.68 },
      })
        .bindTooltip(() => globoZona(zona), { sticky: true })
        .addTo(this.capas.coropleta);
    }
    this.ordenar();
  }

  pintarActivos(activos: readonly ActivoMapa[]): void {
    this.capas.activos.clearLayers();
    this.marcadores.clear();
    this.anillo = null;
    for (const activo of activos) {
      const { idActivo, nivelDano, validada, conflictoPendiente } = activo.properties;
      const [lon, lat] = activo.geometry.coordinates;
      const radio = radioNivel(nivelDano);
      if (conflictoPendiente) {
        L.circleMarker([lat, lon], { radius: radio + 4, color: SENAL, weight: 3, fill: false, interactive: false }).addTo(this.capas.activos);
      }
      const marcador = L.circleMarker([lat, lon], {
        radius: radio,
        color: TINTA,
        weight: validada ? 2 : 1.5,
        dashArray: validada ? undefined : '2 3',
        fillColor: COLOR_NIVEL[nivelDano],
        fillOpacity: 1,
      })
        .bindTooltip(() => globoActivo(activo))
        .on('click', () => this.alElegirActivo(idActivo))
        .addTo(this.capas.activos);
      this.marcadores.set(idActivo, marcador);
    }
    this.marcarActivo(this.elegido);
    this.ordenar();
  }

  pintarZona(vigente: ZonaDibujo | null, anteriores: readonly ZonaDibujo[]): void {
    this.capas.zona.clearLayers();
    for (const zona of anteriores) {
      this.dibujarZona(zona, { color: TINTA, weight: 1.5, dashArray: '5 6', fill: false });
    }
    if (vigente) {
      this.dibujarZona(vigente, { color: TINTA, weight: 3.5, fillColor: TINTA, fillOpacity: 0.05 });
    }
    this.ordenar();
  }

  private dibujarZona(zona: ZonaDibujo, estilo: L.PathOptions): void {
    L.geoJSON(zona.geometria as GeoJsonObject, { style: estilo, interactive: false } as L.GeoJSONOptions).addTo(this.capas.zona);
  }

  marcarActivo(idActivo: string | null): void {
    this.elegido = idActivo;
    this.anillo?.remove();
    this.anillo = null;
    const marcador = idActivo ? this.marcadores.get(idActivo) : undefined;
    if (marcador) {
      this.anillo = L.circleMarker(marcador.getLatLng(), {
        radius: marcador.getRadius() + 7,
        color: TINTA,
        weight: 3,
        fill: false,
        interactive: false,
      }).addTo(this.capas.activos);
    }
  }

  mostrar(capa: NombreCapa, visible: boolean): void {
    if (visible) {
      this.mapa?.addLayer(this.capas[capa]);
      this.ordenar();
    } else {
      this.mapa?.removeLayer(this.capas[capa]);
    }
  }

  encuadrar(): void {
    const limites = L.latLngBounds([]);
    for (const capa of Object.values(this.capas)) {
      if (this.mapa?.hasLayer(capa) && capa.getLayers().length > 0) {
        limites.extend(capa.getBounds());
      }
    }
    if (limites.isValid()) {
      this.mapa?.fitBounds(limites, { padding: [24, 24], maxZoom: 13 });
    }
  }

  private ordenar(): void {
    for (const nombre of ['coropleta', 'zona', 'activos'] as const) {
      if (this.mapa?.hasLayer(this.capas[nombre])) {
        this.capas[nombre].bringToFront();
      }
    }
  }

  destruir(): void {
    this.observador?.disconnect();
    this.mapa?.remove();
    this.mapa = null;
  }
}
