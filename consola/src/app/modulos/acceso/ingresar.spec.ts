import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Sesion } from '../../nucleo/sesion';
import { OidcFalso, proveedoresOidc } from '../../pruebas/apoyo';
import { Ingresar } from './ingresar';

describe('Ingresar', () => {
  let oidc: OidcFalso;
  let fixture: ComponentFixture<Ingresar>;

  beforeEach(async () => {
    sessionStorage.clear();
    oidc = new OidcFalso();
    TestBed.configureTestingModule({ imports: [Ingresar], providers: [provideRouter([]), ...proveedoresOidc(oidc)] });
    fixture = TestBed.createComponent(Ingresar);
    fixture.componentRef.setInput('volver', '/reconstruccion/consolidado');
    await fixture.whenStable();
  });

  it('el botón va a Keycloak y conserva el destino', () => {
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
    expect(oidc.authorize).toHaveBeenCalledOnce();
    expect(TestBed.inject(Sesion).tomarDestino()).toBe('/reconstruccion/consolidado');
  });

  it('muestra el aviso de la sesión', async () => {
    TestBed.inject(Sesion).aviso.set('Su sesión venció.');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('Su sesión venció.');
  });
});
