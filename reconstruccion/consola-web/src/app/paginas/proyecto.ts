import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { LineaEstados } from '../componentes/linea-estados';
import { Api, leerError } from '../nucleo/api';
import { FechaHoraPipe, PesosPipe } from '../nucleo/formato';
import { ErrorApi, Historial, NOMBRE_ESTADO, Proyecto } from '../nucleo/modelos';
import { Sesion } from '../nucleo/sesion';

const NOMBRE_CAMBIO: Record<string, string> = {
  Creacion: 'Creación', Presupuesto: 'Presupuesto', Fuente: 'Fuente de financiación', Responsable: 'Responsable',
  Cronograma: 'Cronograma', Contratista: 'Contratista', Estado: 'Estado', Avance: 'Avance', Costo: 'Costo ejecutado', Documento: 'Documento',
};

/** Detalle de un proyecto con las operaciones que el rol y el estado permiten (CU-03 a CU-10). */
@Component({
  selector: 'app-detalle-proyecto',
  imports: [FormsModule, RouterLink, LineaEstados, PesosPipe, FechaHoraPipe],
  templateUrl: './proyecto.html',
  styleUrl: './proyecto.css',
})
export class DetalleProyecto implements OnInit {
  readonly id = input.required<string>();

  private readonly api = inject(Api);
  protected readonly sesion = inject(Sesion);
  protected readonly estados = NOMBRE_ESTADO;
  protected readonly cambios = NOMBRE_CAMBIO;

  protected readonly proyecto = signal<Proyecto | null>(null);
  protected readonly historial = signal<Historial | null>(null);
  protected readonly error = signal<ErrorApi | null>(null);
  protected readonly confirmacion = signal('');
  protected readonly ocupado = signal(false);

  protected readonly abierto = computed(() => {
    const p = this.proyecto();
    return !!p && p.estado !== 'Finalizado' && p.estado !== 'Cancelado';
  });
  protected readonly esMiProyecto = computed(() => {
    const p = this.proyecto();
    return !!p && this.sesion.esResponsable() && p.responsable === this.sesion.perfil().usuario;
  });
  protected readonly veHistorial = computed(() => !this.sesion.esResponsable());

  // Formularios
  protected presupuesto: number | null = null;
  protected fuente = '';
  protected inicio = '';
  protected fin = '';
  protected justificacionPresupuesto = '';
  protected responsable = '';
  protected justificacionResponsable = '';
  protected contratista = '';
  protected avance: number | null = null;
  protected costo: number | null = null;
  protected concepto = '';
  protected justificacionAvance = '';
  protected justificacionCancelar = '';
  protected fechaConsulta = '';
  private archivo: File | null = null;

  ngOnInit(): void {
    this.api.proyecto(this.id()).subscribe({
      next: (p) => { this.proyecto.set(p); this.cargarHistorial(); },
      error: (r) => this.error.set(leerError(r)),
    });
  }

  private ejecutar(operacion: Observable<Proyecto>, mensaje: string, alTerminar?: () => void): void {
    this.ocupado.set(true);
    this.error.set(null);
    this.confirmacion.set('');
    operacion.subscribe({
      next: (p) => {
        this.proyecto.set(p);
        this.confirmacion.set(mensaje);
        this.ocupado.set(false);
        alTerminar?.();
        this.cargarHistorial();
      },
      error: (r) => { this.error.set(leerError(r)); this.ocupado.set(false); },
    });
  }

  private cargarHistorial(): void {
    if (!this.veHistorial()) {
      return;
    }
    const fecha = this.fechaConsulta ? new Date(this.fechaConsulta).toISOString() : undefined;
    this.api.historial(this.id(), fecha).subscribe({
      next: (h) => this.historial.set(h),
      error: (r) => this.error.set(leerError(r)),
    });
  }

  protected guardarPresupuesto(): void {
    const cuerpo = {
      presupuesto: this.presupuesto,
      fuenteFinanciacion: this.fuente.trim() || null,
      inicio: this.inicio || null,
      fin: this.fin || null,
      justificacion: this.justificacionPresupuesto,
    };
    this.ejecutar(this.api.asignarPresupuesto(this.id(), cuerpo), 'Presupuesto guardado.', () => {
      this.presupuesto = null; this.fuente = ''; this.inicio = ''; this.fin = ''; this.justificacionPresupuesto = '';
    });
  }

  protected guardarResponsable(): void {
    this.ejecutar(this.api.asignarResponsable(this.id(), this.responsable, this.justificacionResponsable), 'Responsable guardado.', () => {
      this.responsable = ''; this.justificacionResponsable = '';
    });
  }

  protected aprobar(): void {
    this.ejecutar(this.api.transicion(this.id(), 'APROBAR'), 'Proyecto aprobado.');
  }

  protected iniciar(): void {
    this.ejecutar(this.api.transicion(this.id(), 'INICIAR', { contratista: this.contratista }), 'Ejecución iniciada.', () => (this.contratista = ''));
  }

  protected registrarAvance(): void {
    const cuerpo = { avance: this.avance, costo: this.costo, concepto: this.concepto.trim() || null, justificacion: this.justificacionAvance.trim() || null };
    this.ejecutar(this.api.registrarAvance(this.id(), cuerpo), 'Avance y costo registrados.', () => {
      this.avance = null; this.costo = null; this.concepto = ''; this.justificacionAvance = '';
    });
  }

  protected finalizar(): void {
    this.ejecutar(this.api.transicion(this.id(), 'FINALIZAR'), 'Proyecto finalizado.');
  }

  protected cancelar(): void {
    this.ejecutar(this.api.transicion(this.id(), 'CANCELAR', { justificacion: this.justificacionCancelar }), 'Proyecto cancelado.', () => (this.justificacionCancelar = ''));
  }

  protected elegirArchivo(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    this.archivo = campo.files && campo.files.length > 0 ? campo.files[0] : null;
  }

  protected adjuntar(campo: HTMLInputElement): void {
    if (!this.archivo) {
      this.error.set({ codigo: 'SIN_ARCHIVO', mensaje: 'Elija un archivo antes de adjuntar.' });
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    this.confirmacion.set('');
    this.api.adjuntarDocumento(this.id(), this.archivo).subscribe({
      next: (d) => {
        this.confirmacion.set(`Documento adjunto: ${d.nombre}. Huella ${d.hash.slice(0, 12)}…`);
        this.ocupado.set(false);
        this.archivo = null;
        campo.value = '';
        this.ngOnInit();
      },
      error: (r) => { this.error.set(leerError(r)); this.ocupado.set(false); },
    });
  }

  protected consultarFecha(): void {
    this.cargarHistorial();
  }

  protected quitarFecha(): void {
    this.fechaConsulta = '';
    this.cargarHistorial();
  }
}
