-- Datos de prueba de las copias locales. Los carga el migrador con "migrar --sembrar" (solo en desarrollo).
-- En la plataforma integrada estos datos llegan como eventos de Gestión de Desastres (EV-03, EV-06) y de
-- Evaluación de Daños (EV-08); los consumidores se agregan en el paso 4 (DA-01).
-- Es repetible: no duplica filas si se ejecuta otra vez.
-- Códigos DIVIPOLA del DANE: Armenia 63001 y Calarcá 63130 (Quindío), La Virginia 66400 (Risaralda).

INSERT INTO desastres_locales (id, nombre, estado) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Sismo del Eje Cafetero', 'EN_RECONSTRUCCION'),
    ('22222222-2222-2222-2222-222222222222', 'Inundación del río Cauca', 'EN_RECUPERACION')
ON CONFLICT (id) DO NOTHING;

INSERT INTO evaluaciones_validadas (id, id_desastre, id_activo, nombre_activo, tipo_activo, nivel_dano, municipio, codigo_municipio, estado) VALUES
    ('aaaaaaaa-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000001', 'Escuela La Esperanza',       'Escuela',         'Grave',            'Armenia',     '63001', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000002', 'Puente El Alambrado',        'Puente',          'DestruccionTotal', 'Armenia',     '63001', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000003', 'Vía Armenia - Calarcá',      'Carretera',       'Grave',            'Armenia',     '63001', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000003', 'Vía Armenia - Calarcá',      'Carretera',       'Grave',            'Calarcá',     '63130', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000005', 'Centro de salud La Patria',  'Hospital',        'Leve',             'Armenia',     '63001', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000006', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000006', 'Colegio San José',           'Escuela',         'DestruccionTotal', 'Calarcá',     '63130', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000007', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000007', 'Acueducto veredal El Caimo', 'Sistema de agua', 'Grave',            'Armenia',     '63001', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000008', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000008', 'Hospital San Juan de Dios',  'Hospital',        'Moderado',         'Armenia',     '63001', 'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000009', '22222222-2222-2222-2222-222222222222', 'bbbbbbbb-0000-0000-0000-000000000009', 'Puente La Virginia',         'Puente',          'Grave',            'La Virginia', '66400', 'VALIDADA')
ON CONFLICT (id) DO NOTHING;
