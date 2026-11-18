import { Component } from '@angular/core';

/** Para quien inició sesión con un rol que no trabaja en la consola, como el evaluador de campo. */
@Component({
  selector: 'app-sin-acceso',
  template: `
    <h1>Sin secciones disponibles</h1>
    <p class="vacio">Su rol no tiene secciones en la consola de coordinación. Si trabaja en campo, use la aplicación móvil.</p>
  `,
  styles: `h1 { margin-bottom: 1.5rem; }`,
})
export class SinAcceso {}
