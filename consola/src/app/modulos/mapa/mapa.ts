import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, WritableSignal, afterNextRender, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Observable } from 'rxjs';
import { leerError } from '../../nucleo/api';
import { FechaHoraPipe } from '../../nucleo/formato';
import { ApiMapa } from './api-mapa';
import { BarraTiempo, MomentoDesastre } from './barra-tiempo';
import { COLOR_NIVEL, NOMBRE_NIVEL, cantidad, geometriaDe, instantesDe, legible, nombreEstado, nombreValidacion, numero, textoZona } from './formato-mapa';
import { FABRICA_LIENZO, LienzoMapa } from './lienzo-mapa';
import {
  ActivoMapa,
  EstadoEnInstante,
  EvolucionDesastre,
  FilaDesastre,
  MapaDanos,
  MapaZonas,
  NIVELES,
  NivelDano,
  NombreCapa,
  Respuesta,
  ZonaDano,
  ZonaDibujo,
} from './modelos-mapa';
import { PanelActivo } from './panel-activo';

interface Lectura<T> {
  datos: T | null;
  degradado: boolean;
  error: string | null;
  cargando: boolean;
}

const sinLectura = <T>(): Lectura<T> => ({ datos: null, degradado: false, error: null, cargando: false });

/** Filas de la tabla de activos: la alternativa al clic en el mapa para quien usa el teclado. */
const MAXIMO_FILAS = 100;

/**
 * Mapa de daños y desastres (DA-13, RF-ED-11, RF-GD-17): coropleta del índice de daño, activos por nivel y la zona del
 * desastre con su barra de tiempo. Cada capa dice de cuándo es su dato (AD-16) y avisa si es la última conocida (CON-6).
 */
@Component({
  selector: 'app-mapa',
  imports: [BarraTiempo, FechaHoraPipe, PanelActivo],
  templateUrl: './mapa.html',
  styleUrl: './mapa.css',
})
export class PantallaMapa {
  private readonly api = inject(ApiMapa);
  private readonly fabricaLienzo = inject(FABRICA_LIENZO);
  private readonly contenedor = viewChild.required<ElementRef<HTMLElement>>('lienzo');
  private readonly lienzo = signal<LienzoMapa | null>(null);
  private readonly encuadrePendiente = signal(false);

  readonly niveles = NIVELES;
  readonly maximoFilas = MAXIMO_FILAS;
  readonly numero = numero;
  readonly cantidad = cantidad;
  readonly textoZona = textoZona;
  readonly legible = legible;
  readonly validacion = nombreValidacion;
  readonly nombreNivel = (nivel: NivelDano) => NOMBRE_NIVEL[nivel];
  readonly colorNivel = (nivel: NivelDano) => COLOR_NIVEL[nivel];

  readonly desastres = signal<FilaDesastre[]>([]);
  readonly errorDesastres = signal<string | null>(null);
  readonly desastresDegradados = signal<string | null | false>(false);
  readonly idDesastre = signal('');
  readonly nivelMinimo = signal<NivelDano>('SIN_DANO');
  readonly soloValidadas = signal(false);
  readonly visibles = signal<Record<NombreCapa, boolean>>({ coropleta: true, activos: true, zona: true });
  readonly idActivo = signal<string | null>(null);
  readonly indice = signal(0);

  readonly danos = signal(sinLectura<MapaDanos>());
  readonly zonas = signal(sinLectura<MapaZonas>());
  readonly evolucion = signal(sinLectura<EvolucionDesastre>());
  readonly enInstante = signal(sinLectura<EstadoEnInstante>());

  readonly zonasDano = computed(() => (this.danos().datos?.features ?? []).filter((f): f is ZonaDano => f.properties.tipo === 'zona'));
  readonly activos = computed(() => (this.danos().datos?.features ?? []).filter((f): f is ActivoMapa => f.properties.tipo === 'activo'));
  readonly activosEnTabla = computed(() => [...this.activos()].sort((a, b) => b.properties.peso - a.properties.peso).slice(0, MAXIMO_FILAS));

  readonly cambios = computed(() => this.evolucion().datos?.cambios ?? []);
  readonly versiones = computed(() => this.evolucion().datos?.zonas ?? []);
  readonly instantes = computed(() => instantesDe(this.cambios()));
  /** El instante que se consulta; nulo en la última parada, que es el estado actual. */
  readonly instanteElegido = computed(() => (this.indice() < this.instantes().length - 1 ? this.instantes()[this.indice()] : null));

  readonly zonaDesastre = computed(() => this.zonas().datos?.features.find((f) => f.properties.idDesastre === this.idDesastre()) ?? null);
  readonly fila = computed(() => this.desastres().find((d) => d.id === this.idDesastre()) ?? null);

  readonly momento = computed<MomentoDesastre | null>(() => {
    if (this.instanteElegido()) {
      const m = this.enInstante().datos;
      return m ? { severidad: m.severidad, estado: m.estado, version: m.zona?.version ?? null, municipios: m.zona?.codigosMunicipio.length ?? 0 } : null;
    }
    const z = this.zonaDesastre()?.properties;
    const f = this.fila();
    return f ? { severidad: f.severidad, estado: f.estado, version: z?.zonaVersion ?? null, municipios: z?.municipios.length ?? 0 } : null;
  });

  private readonly zonaDibujada = computed<ZonaDibujo | null>(() => {
    if (this.instanteElegido()) {
      const zona = this.enInstante().datos?.zona;
      const geometria = zona ? geometriaDe(zona) : null;
      return zona && geometria ? { version: zona.version, geometria } : null;
    }
    const zona = this.zonaDesastre();
    return zona ? { version: zona.properties.zonaVersion, geometria: zona.geometry } : null;
  });

  private readonly zonasAnteriores = computed<ZonaDibujo[]>(() => {
    const actual = this.zonaDibujada()?.version ?? Number.POSITIVE_INFINITY;
    return this.versiones().flatMap((v) => {
      const geometria = v.version < actual ? geometriaDe(v) : null;
      return geometria ? [{ version: v.version, geometria }] : [];
    });
  });

  readonly datoAlZona = computed(() => (this.instanteElegido() ? this.enInstante().datos?.instante : this.zonaDesastre()?.properties.datoAl) ?? null);

  readonly degradadas = computed(() => {
    const avisos: { capa: string; datoAl: string | null }[] = [];
    if (this.danos().degradado) {
      avisos.push({ capa: 'Índice de daño y activos', datoAl: this.danos().datos?.datoAl ?? null });
    }
    if (this.zonas().degradado || this.evolucion().degradado || this.enInstante().degradado) {
      avisos.push({ capa: 'Zona del desastre', datoAl: this.datoAlZona() });
    }
    return avisos;
  });

  readonly errores = computed(() => {
    const avisos = [
      { capa: 'Desastres', error: this.errorDesastres() },
      { capa: 'Índice de daño y activos', error: this.danos().error },
      { capa: 'Zona del desastre', error: this.zonas().error },
      { capa: 'Evolución del desastre', error: this.evolucion().error ?? this.enInstante().error },
    ];
    return avisos.filter((a): a is { capa: string; error: string } => a.error !== null);
  });

  constructor() {
    const destruir = inject(DestroyRef);
    afterNextRender(() => {
      const lienzo = this.fabricaLienzo();
      lienzo.montar(this.contenedor().nativeElement, (id) => this.idActivo.set(id));
      this.lienzo.set(lienzo);
      destruir.onDestroy(() => lienzo.destruir());
    });

    const tablero = this.api.desastres().subscribe({
      next: (r) => this.recibirDesastres(r),
      error: (e: HttpErrorResponse) => this.errorDesastres.set(leerError(e).mensaje),
    });
    destruir.onDestroy(() => tablero.unsubscribe());

    this.leerAlCambiar();
    this.dibujarAlCambiar();
  }

  private recibirDesastres({ cuerpo, degradado }: Respuesta<{ datos: FilaDesastre[]; datoAl: string | null }>): void {
    // Primero los que siguen en curso y, entre ellos, los que cambiaron hace menos.
    const orden = [...cuerpo.datos].sort(
      (a, b) => Number(a.estado === 'CERRADO') - Number(b.estado === 'CERRADO') || Date.parse(b.datoAl ?? '') - Date.parse(a.datoAl ?? ''),
    );
    this.desastres.set(orden);
    this.desastresDegradados.set(degradado ? cuerpo.datoAl : false);
    if (orden.length > 0) {
      this.elegirDesastre(orden[0].id);
    }
  }

  private leerAlCambiar(): void {
    effect((alLimpiar) => {
      const desastre = this.idDesastre();
      if (!desastre) return;
      const filtro = { desastre, nivelMinimo: this.nivelMinimo(), soloValidadas: this.soloValidadas() };
      alLimpiar(this.leer(this.danos, this.api.danos(filtro)));
    });

    effect((alLimpiar) => {
      if (!this.idDesastre()) return;
      alLimpiar(this.leer(this.zonas, this.api.zonas()));
    });

    effect((alLimpiar) => {
      const desastre = this.idDesastre();
      if (!desastre) return;
      alLimpiar(this.leer(this.evolucion, this.api.evolucionDesastre(desastre), (e) => this.indice.set(Math.max(0, instantesDe(e.cambios).length - 1))));
    });

    effect((alLimpiar) => {
      const instante = this.instanteElegido();
      const desastre = untracked(this.idDesastre);
      if (!instante) {
        this.enInstante.set(sinLectura());
        return;
      }
      alLimpiar(this.leerConvertido(this.enInstante, this.api.evolucionDesastre(desastre, instante), (e) => e.enInstante));
    });
  }

  private leer<T>(destino: WritableSignal<Lectura<T>>, fuente: Observable<Respuesta<T>>, alLlegar?: (datos: T) => void): () => void {
    return this.leerConvertido(destino, fuente, (d) => d, alLlegar);
  }

  private leerConvertido<C, T>(
    destino: WritableSignal<Lectura<T>>,
    fuente: Observable<Respuesta<C>>,
    convertir: (cuerpo: C) => T | null,
    alLlegar?: (datos: T) => void,
  ): () => void {
    destino.update((l) => ({ ...l, cargando: true, error: null }));
    const lectura = fuente.subscribe({
      next: ({ cuerpo, degradado }) => {
        const datos = convertir(cuerpo);
        destino.set({ datos, degradado, error: null, cargando: false });
        if (datos !== null) alLlegar?.(datos);
      },
      error: (e: HttpErrorResponse) => destino.set({ ...sinLectura<T>(), error: leerError(e).mensaje }),
    });
    return () => lectura.unsubscribe();
  }

  private dibujarAlCambiar(): void {
    effect(() => this.lienzo()?.pintarCoropleta(this.zonasDano()));
    effect(() => this.lienzo()?.pintarActivos(this.activos()));
    effect(() => this.lienzo()?.pintarZona(this.zonaDibujada(), this.zonasAnteriores()));
    effect(() => this.lienzo()?.marcarActivo(this.idActivo()));
    effect(() => {
      const lienzo = this.lienzo();
      const visibles = this.visibles();
      if (lienzo) {
        for (const capa of ['coropleta', 'activos', 'zona'] as const) {
          lienzo.mostrar(capa, visibles[capa]);
        }
      }
    });
    // Se encuadra una vez por desastre, cuando ya llegaron sus capas: filtrar no mueve el mapa.
    effect(() => {
      const lienzo = this.lienzo();
      const listas = [this.danos(), this.zonas()].every((l) => !l.cargando && (l.datos !== null || l.error !== null));
      if (lienzo && listas && this.encuadrePendiente()) {
        lienzo.encuadrar();
        this.encuadrePendiente.set(false);
      }
    });
  }

  elegirDesastre(id: string): void {
    this.idActivo.set(null);
    this.indice.set(0);
    this.danos.set(sinLectura());
    this.zonas.set(sinLectura());
    this.evolucion.set(sinLectura());
    this.enInstante.set(sinLectura());
    this.encuadrePendiente.set(true);
    this.idDesastre.set(id);
  }

  cambiarCapa(capa: NombreCapa, visible: boolean): void {
    this.visibles.update((v) => ({ ...v, [capa]: visible }));
  }

  marcada(evento: Event): boolean {
    return (evento.target as HTMLInputElement).checked;
  }

  valorDe(evento: Event): string {
    return (evento.target as HTMLSelectElement).value;
  }

  nivelDe(evento: Event): NivelDano {
    return this.valorDe(evento) as NivelDano;
  }

  nombreDesastre(d: FilaDesastre): string {
    const nombre = d.nombre ?? 'Desastre sin nombre';
    return d.estado === 'CERRADO' ? `${nombre} (${nombreEstado(d.estado).toLowerCase()})` : nombre;
  }
}
