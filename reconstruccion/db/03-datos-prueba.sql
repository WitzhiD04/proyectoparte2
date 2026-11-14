-- Datos de prueba para las copias locales. En la plataforma completa estos datos llegan como eventos
-- de Gestión de Desastres y de Evaluación de Daños; esos módulos todavía no están implementados.
\connect reconstruccion

INSERT INTO desastres_locales (id, nombre, estado) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Sismo del Eje Cafetero', 'EN_RECONSTRUCCION'),
    ('22222222-2222-2222-2222-222222222222', 'Inundación del río Cauca', 'EN_RECUPERACION');

INSERT INTO evaluaciones_validadas (id, id_desastre, id_activo, nombre_activo, tipo_activo, nivel_dano, municipio, estado) VALUES
    ('aaaaaaaa-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000001', 'Escuela La Esperanza',        'Escuela',   'Grave',            'Armenia',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000002', 'Puente El Alambrado',         'Puente',    'DestruccionTotal', 'Armenia',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000003', 'Vía Armenia - Calarcá',       'Carretera', 'Grave',            'Armenia',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000003', 'Vía Armenia - Calarcá',       'Carretera', 'Grave',            'Calarcá',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000005', 'Centro de salud La Patria',   'Hospital',  'Leve',             'Armenia',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000006', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000006', 'Colegio San José',            'Escuela',   'DestruccionTotal', 'Calarcá',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000007', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000007', 'Acueducto veredal El Caimo',  'Sistema de agua', 'Grave',      'Armenia',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000008', '11111111-1111-1111-1111-111111111111', 'bbbbbbbb-0000-0000-0000-000000000008', 'Hospital San Juan de Dios',   'Hospital',  'Moderado',         'Armenia',  'VALIDADA'),
    ('aaaaaaaa-0000-0000-0000-000000000009', '22222222-2222-2222-2222-222222222222', 'bbbbbbbb-0000-0000-0000-000000000009', 'Puente La Virginia',          'Puente',    'Grave',            'La Virginia', 'VALIDADA');
