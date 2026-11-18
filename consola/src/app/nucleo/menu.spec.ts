import { inicioPara, menuPara, moduloDe } from './menu';

describe('menú por roles', () => {
  const etiquetas = (...roles: Parameters<typeof menuPara>[0]) => menuPara(roles).map((e) => e.etiqueta);

  it('la autoridad ve todo lo de esta entrega', () => {
    expect(etiquetas('autoridad')).toEqual(['Evaluaciones candidatas', 'Proyectos', 'Consolidado del programa', 'Mapa', 'Evaluaciones']);
  });

  it('el responsable ve sus proyectos y el mapa, sin candidatas ni consolidado', () => {
    expect(etiquetas('responsable-proyecto')).toEqual(['Proyectos', 'Mapa']);
  });

  it('el auditor ve proyectos, consolidado y mapa', () => {
    expect(etiquetas('auditor')).toEqual(['Proyectos', 'Consolidado del programa', 'Mapa']);
  });

  it('un organismo no ve Reconstrucción', () => {
    expect(etiquetas('organismo')).toEqual(['Mapa', 'Evaluaciones']);
  });

  it('el evaluador trabaja en la app móvil y no tiene secciones', () => {
    expect(etiquetas('evaluador')).toEqual([]);
    expect(inicioPara(['evaluador'])).toBeNull();
  });

  it('el inicio es Proyectos para quien opera en Reconstrucción y la primera sección para los demás', () => {
    expect(inicioPara(['autoridad'])).toBe('/reconstruccion/proyectos');
    expect(inicioPara(['organismo'])).toBe('/mapa');
  });

  it('reconoce el módulo de una URL', () => {
    expect(moduloDe('/reconstruccion/proyectos/123?x=1')).toBe('Reconstrucción');
    expect(moduloDe('/mapa')).toBe('Mapa');
    expect(moduloDe('/evaluaciones')).toBe('Evaluaciones');
    expect(moduloDe('/ingresar')).toBeNull();
  });
});
