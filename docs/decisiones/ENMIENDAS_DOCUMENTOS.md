# Enmiendas propuestas a los documentos

Complemento de [REVISION_ARQUITECTURA.md](REVISION_ARQUITECTURA.md). **No se modificó ningún documento.** Este archivo lista lo que habría que corregir o agregar al SRS 1.1, al SAD 1.1, al ATAM 1.1 y al Documento de diseño del módulo de Reconstrucción, para que coincidan con la arquitectura que se va a implementar.

Fecha: 16 de noviembre de 2026.

## Criterios

- **Toda enmienda cumple CON-7:** cita su driver, la alternativa descartada y el costo aceptado, o remite a la decisión DA-nn del documento de revisión, que los tiene.
- **Los eventos siguen la regla de evolución que propone el ATAM para R-03:** "el contrato solo admite cambios aditivos, y un cambio semántico es una versión nueva del evento canónico, que convive con la anterior". Todas las enmiendas de eventos de este archivo son **aditivas**: agregan campos, consumidores o eventos nuevos. Ninguna cambia el significado de un campo existente.
- **Tipos de enmienda:**
  - **Correctiva:** el documento se contradice o deja un flujo sin cerrar.
  - **Aditiva:** el documento no lo cubre y la implementación lo necesita.
  - **Aclaratoria:** el documento es ambiguo.
- La columna "Origen" remite al hallazgo (H-nn) o a la decisión (DA-nn) del documento de revisión.

## Resumen

| ID | Documento | Lugar | Tipo | Origen |
|---|---|---|---|---|
| E-01 | SAD | Cuadros 15 y 16 (candidatas y consumidores de EV-08) | Correctiva | H-04, DA-01 |
| E-02 | SAD | Cuadro 16 (campos de EV-08) | Aditiva | H-04, DA-07 |
| E-03 | SAD | Cuadro 16 (campos de EV-09) | Correctiva | H-03, DA-08 |
| E-04 | SAD | Cuadros 14 y 16 (EV-24, entrada de SE-7) | Correctiva | H-05, DA-11 |
| E-05 | SAD | Cuadros 15 y 16 (EV-22, conflicto resuelto) | Correctiva | H-05, DA-09 |
| E-06 | SAD | Cuadro 16 (EV-26, evaluación actualizada en el servidor) | Correctiva | H-05 |
| E-07 | SAD y Diseño RC | Cuadro 16 (consumidores de EV-18 y EV-20) y regla de cierre | Correctiva | H-06, DA-10 |
| E-08 | SAD | §5.2 (enumeraciones canónicas) | Aditiva | H-07, DA-02 |
| E-09 | SAD | Cuadro 15 (rutas) | Correctiva | H-08, DA-12 |
| E-10 | SAD | Cuadro 16 (EV-18, EV-19, EV-20 y SE-6) | Correctiva | H-31 |
| E-11 | SAD | Cuadros 14 y 16 (EV-21, capa geográfica de SE-2) | Aditiva | DA-07 |
| E-12 | SRS | Cuadro 9 y RF-ED-01 (municipio del activo) | Aditiva | H-19, DA-07 |
| E-13 | SAD | Cuadro 16 (EV-10) y Cuadro 4 (dueño de la bandeja de revisión) | Correctiva | DA-09 |
| E-14 | SRS | RF-ED-09 (definición de "fusionar") | Aclaratoria | DA-09 |
| E-15 | SAD y ATAM | Cuadro 16 (EV-23, estado de integración). Estado de R-10 | Aditiva | DA-11 |
| E-16 | SAD | Cuadro 16 (EV-25, catálogo de tipos de activo actualizado) | Aditiva | DA-18 |
| E-17 | SAD | Anexo B nuevo (tecnologías de la segunda entrega) | Aditiva | §7 |
| E-18 | SAD y ATAM | §5.2 (`secuenciaProductor` en el sobre). Estado de R-02 | Aditiva | DA-02 |
| E-19 | SAD | AD-45 (bandeja en todo productor) y Cuadro 5 | Aditiva | DA-04 |
| E-20 | Diseño RC | Regla de aprobación (autoridad nacional y territorio) | Aclaratoria | H-32, DA-14 |
| E-21 | SAD | AD-17 (alcance de la caché de geometrías) | Aclaratoria | DA-08 |
| E-22 | SAD | Anexo A (simplificaciones superadas) | Aclaratoria | — |
| E-23 | SAD | AD-02 y Cuadro 5 (colas acotadas: política de desborde y DLQ) | Aclaratoria | H-41, H-42, DA-03 |
| E-24 | SAD y ATAM | AD-26 (mecanismo de conmutación) y R-09 | Aclaratoria | DA-16 |
| E-25 | SAD | Cuadro 16 (EV-27, organismos del desastre) | Aditiva | DA-09, DA-10 |
| E-26 | SAD | Cuadro 15 (descarga filtrada e incremental) | Aclaratoria | H-16, DA-19 |
| E-27 | SAD | §7.1 (cifrado en tránsito entre servicios) | Aclaratoria | DA-15 |
| E-28 | Diseño RC | Contexto y CU-01 (el origen de las candidatas) | Correctiva | DA-01 |
| E-29 | SAD | Cuadro 16 (Evaluación de Daños consume EV-03, EV-05, EV-06 y EV-21) | Correctiva | Paso 1, hallazgo 5 |
| E-30 | SAD | Cuadro 16 (campos adicionales de EV-03, EV-06, EV-10, EV-18, EV-19 y EV-20) | Aditiva | Paso 1 |
| E-31 | SAD | §5.2 y AD-45 (la secuencia del productor la asigna el relevo, sin huecos) | Aclaratoria | Paso 1, hallazgo 1 |
| E-32 | SAD | Cuadro 5 (límite de entregas: lo aplica el consumidor) | Aclaratoria | Paso 1 |
| E-33 | SAD | Cuadro 16 (EV-18 lleva el autor de la creación) | Aditiva | Paso 3 |
| E-34 | SAD y ATAM | AD-21 (Auditoría registra los tipos de evento que no conoce). R-03 | Aditiva | Paso 4 |
| E-35 | SAD y SRS | Cuadro 15 y RN-3 (rutas de configuración del ciclo de vida y perfil mínimo por nivel) | Aditiva | Paso 5 |
| E-36 | SAD y Diseño RC | Cuadro 16 (Reconstrucción consume EV-21) | Aditiva | Paso 5 |
| E-37 | SAD | Cuadro 16 y AD-13 (Notificación: EV-07, EV-28 y EV-29) | Aditiva | Notificación |
| E-38 | SAD | Cuadro 15 (el marcador `desde` es un instante del servidor) | Correctiva | DA-19 |
| E-39 | Diseño RC | CU-01 a CU-11 (jurisdicción de la autoridad) | Aclaratoria | DA-14 |
| E-40 | SAD | Cuadro 15 y DA-08 (evolución por zona) | Aditiva | A4 |
| E-41 | SAD | DA-13 (tablero de daños en Razor Pages) | Excepción acotada | C1 |
| E-42 | SRS y SAD | RF-GD-02 y Cuadro 15 (registro del desastre en campo con id del cliente, AD-48) | Aditiva | Cierre |
| E-43 | SAD | Vistas lógica y física (Proveedor de identidad, AD-49) | Aditiva | DA-15 |
| E-44 | SRS, SAD y ATAM | CON-9, AD-66, L-11, vistas y QA-09 | Aditiva | Restricción nueva del profesor (entrega 2) |

---

## Detalle

### E-01 · Reconstrucción consume EV-08 y no consulta a Consulta de Daños

**SAD, Cuadro 15.** Fila `GET /v1/reconstruccion/candidatas`.

- **Texto actual:** "Evaluaciones con daño grave o destrucción total de un desastre en reconstrucción. Usa el microservicio de Consulta de Daños."
- **Texto propuesto:** "Evaluaciones validadas con daño grave o destrucción total de un desastre en reconstrucción que no tienen proyecto vigente. Se atiende desde la copia local de Reconstrucción, alimentada por EV-03, EV-06 y EV-08 (AD-46)."

**SAD, Cuadro 16.** EV-08 "Evaluación validada".

- **Consumidores actuales:** Consulta de Daños.
- **Consumidores propuestos:** Consulta de Daños, **Reconstrucción**.

**SAD, Cuadro 18.** Agregar una decisión **AD-46**: "Reconstrucción mantiene una copia local de los desastres y de las evaluaciones validadas, alimentada por eventos".

- **Drivers:** AD-01, CON-6, QA-04 y FP-08.
- **Alternativa descartada:** la llamada síncrona a Consulta de Daños, porque crea dependencia entre microservicios en tiempo de ejecución y contradice el §3.5: "Ningún microservicio depende de otro".
- **Costo aceptado:** consistencia eventual, con retraso menor que 2 s, y la copia de datos de otro módulo (CON-1).

**Por qué es correctiva:** el SAD se contradice con su propia vista de implementación, y con el Documento de diseño de Reconstrucción (Figura 1: "Evaluación de Daños avisa cuando una evaluación queda validada").

### E-02 · Campos adicionales de EV-08

- **Campos actuales:** `idEvaluacion, idDesastre, tipoActivo, nivelDano, ubicacion, evaluador, organizacion, version`.
- **Campos que se agregan:**
  - `idActivo`: Reconstrucción crea un proyecto por activo (Diseño RC, regla de creación por lote).
  - `referenciaActivo`: el nombre del proyecto.
  - `codigoMunicipio` y `codigoDepartamento` (DIVIPOLA): la jurisdicción de la aprobación (E-20).
  - `estadoValidacion` (`VALIDADA`, `RECHAZADA` o `PENDIENTE`): RF-ED-08 permite volver a pendiente o rechazar, y Reconstrucción debe sacar la evaluación de sus candidatas.
  - `validadoPor` e `instanteValidacion`: RF-ED-08, "registrar quién lo hizo y cuándo".
- **Nota:** EV-08 se emite en cada cambio de validación, no solo cuando la evaluación queda validada. El nombre del evento puede quedar como "Evaluación validada" o cambiarse a "Validación de evaluación cambiada". Recomiendo el segundo, porque es más exacto, y se puede hacer como versión 2 del evento para respetar la regla de R-03.

### E-03 · Campos adicionales de EV-09

- **Campos actuales:** `idEvaluacion, idDispositivo, instanteLocal, instanteServidor, enConflicto, versionesConservadas`.
- **Campos que se agregan:** `idActivo, idDesastre (puede ser nulo: preliminar), tipoActivo, nivelDano, ubicacion, precisionM, codigoMunicipio, estadoEvaluacion, versionEvaluacion, versionActivo`.
- **Por qué es correctiva:** el único consumidor de EV-09 es Consulta de Daños, que debe construir el mapa y el consolidado "con proyecciones alimentadas por eventos" (AD-34). Con los campos actuales no puede hacerlo sin leer el almacén de Evaluación de Daños, y eso lo prohíbe AD-01.

### E-04 · EV-24, avance y contratista reportados por SE-7

**SAD, Cuadro 14.** SE-7 declara "Entrada de avance y contratista", pero el Cuadro 16 no tiene ningún evento para ese flujo.

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-24 | Avance o contratista reportado por SE-7 | Adaptador de SE-7 | Reconstrucción | `idProyecto, tipo (AVANCE / CONTRATISTA), avance, contratista, instanteExterno, claveOrigen, marcaAvance` |

- **Regla en Reconstrucción** (es la que ya está en el Diseño RC): el avance externo "guarda su procedencia" (`SE-7`). Si un valor es menor que el vigente, se registra como corrección con la justificación "reportado por SE-7".
- **Cuadro 17, FP-08:** agregar EV-24.

### E-05 · EV-22, conflicto resuelto

**SAD, Cuadro 15:** existe `POST /v1/conflictos/{id}/resolucion`. **Cuadro 16:** no tiene ningún evento para la resolución.

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-22 | Conflicto o duplicado resuelto | Evaluación de Daños | Consulta de Daños | `idConflicto, decision (PREVALECE_ENTRANTE / PREVALECE_VIGENTE / FUSIONAR / RECHAZAR_DUPLICADO), idEvaluacionVigente, versionVigente, decididoPor, justificacion`, más una copia de los campos de EV-09 de la evaluación que queda vigente |

- **Drivers:**
  - **AD-32:** decide un usuario autorizado.
  - **ATAM, ficha de QA-14 (EA-11):** "auditoría de la decisión humana". Sin el evento, Auditoría no la registra.
  - **AD-34:** el mapa debe reflejar la evaluación que prevalece.

### E-06 · EV-26, evaluación actualizada en el servidor

**Rutas del SAD** que cambian una evaluación sin pasar por la sincronización: `PATCH /v1/evaluaciones/{id}` (RF-ED-07) y `POST /v1/evaluaciones/{id}/asociacion` (AD-29). **Cuadro 16:** no les asigna evento.

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-26 | Evaluación actualizada en el servidor | Evaluación de Daños | Consulta de Daños | `idEvaluacion, motivo (ACTUALIZACION / ASOCIACION), autor`, más los campos de EV-09 |

- **Por qué es correctiva:** una evaluación preliminar asociada después a un desastre nunca aparecería en el mapa de ese desastre.
- **Alternativa:** reutilizar EV-09 agregando `motivo`. Se descarta porque EV-09 describe la sincronización desde un dispositivo (`idDispositivo`, `instanteLocal`), y mezclarla cambia su significado (R-03).

### E-07 · Gestión de Desastres consume EV-18 y EV-20 para la regla de cierre

**Documento de diseño de Reconstrucción**, regla: "El desastre solo puede cerrarse cuando todos sus proyectos están en un estado final".

**SAD:** Gestión de Desastres no consume eventos de Reconstrucción, así que la regla no tiene mecanismo.

**Enmiendas:**

- **SAD, Cuadro 16:** agregar Gestión de Desastres como consumidor de EV-18 y EV-20.
- **SRS, RN-2:** agregar la condición "la transición a Cerrado exige que no haya proyectos de reconstrucción en estado no final".
- **SAD, Cuadro 18:** agregar una decisión **AD-47**: "Gestión de Desastres mantiene un contador local de proyectos no finales por desastre".
  - **Alternativa descartada:** consultar a Reconstrucción de forma síncrona al cerrar, porque es una dependencia síncrona entre microservicios (AD-01).
  - **Costo aceptado:** consistencia eventual. Un proyecto recién finalizado tarda menos de 2 s en permitir el cierre.

### E-08 · Enumeraciones canónicas

**SAD §5.2:** agregar al esquema del evento canónico los valores admitidos:

- `nivelDano`: `SIN_DANO, LEVE, MODERADO, GRAVE, DESTRUCCION_TOTAL` (SRS RN-5, enunciado §4.2).
- `estadoDesastre`: `DETECTADO, REGISTRADO, EN_EVALUACION, EN_ATENCION, EN_RECUPERACION, EN_RECONSTRUCCION, CERRADO` (enunciado §4.1). El catálogo de perfiles (AD-07) solo puede usar estos códigos. Un estado nuevo es un cambio del contrato (R-03).
- `severidad`: `NIVEL_1, NIVEL_2, NIVEL_3, NIVEL_4` (RN-3).
- `estadoProyecto`: `FORMULADO, APROBADO, EN_EJECUCION, FINALIZADO, CANCELADO` (Diseño RC).
- **Instantes:** ISO-8601 en UTC con zona explícita (SRS Cuadro 7, atributo 12).
- **Códigos territoriales:** DIVIPOLA del DANE.

### E-09 · Rutas del Cuadro 15

| Cambio | Ruta | Razón |
|---|---|---|
| Agregar | `GET /v1/proyectos?desastre=`, `GET /v1/proyectos/{id}` | La consola necesita listar y ver el detalle. Hoy existen en el código y no en el SAD |
| Agregar | `PATCH /v1/proyectos/{id}/responsable` | Diseño RC, CU-03. El SAD solo tiene `/presupuesto` |
| Agregar | `POST /v1/proyectos/{id}/documentos` | Diseño RC, CU-07, y RF-RC-06 |
| Agregar | `GET /v1/reconstruccion/desastres` | Los desastres en la copia local de Reconstrucción. **No debe ser `/v1/desastres`**, porque esa ruta es de Gestión de Desastres y chocaría en el gateway |
| Agregar | `GET /v1/evaluaciones?dispositivo=` | Para que el evaluador vea el estado de sus evaluaciones y para verificar QA-14 (existe en el prototipo) |
| Agregar | `GET /v1/consulta-danos/indicadores-revision` | Alarma de R-08 (DA-09) |
| Agregar | `GET /v1/integraciones` | El estado de cada integración (R-10, EV-23) |
| Conservar | `POST /v1/proyectos:lote` y `POST /v1/proyectos/{id}/avance` | **El código se ajusta al SAD** (hoy usa `/lote` y `/avances`) |
| Conservar | `GET /v1/consulta-danos/mapa` | **El código se ajusta** (hoy usa `/v1/mapa-calor` en el servicio de escritura) |

### E-10 · Contenido y consumidores de EV-18, EV-19 y EV-20

- **EV-18 (Proyecto creado):** los campos del SAD ya son correctos (`idsEvaluacionesOrigen, presupuesto, fuenteFinanciacion, responsable, cronograma`). El que se debe ajustar es el **código**. Se agrega `codigosMunicipio` y `alcance`, que SE-7 necesita para registrar la obra.
- **EV-19 (Presupuesto cambiado):** aclarar que incluye `presupuesto` y `fuenteFinanciacion`, que es lo que le interesa a SE-6. Hoy el código emite un solo evento `asignacion_cambiada` que además incluye el responsable y el cronograma.
  - **Propuesta:** EV-19 cubre el presupuesto y la fuente.
  - El responsable, el cronograma y el contratista van en **EV-20**, con un campo `tipoCambio`.
- **EV-20 (Estado o avance de proyecto cambiado):**
  - Agregar el **adaptador de SE-6** como consumidor. El Diseño RC dice "presupuesto **y costos** al sistema financiero", y el costo ejecutado viaja en EV-20.
  - Agregar los campos `tipoCambio`, `procedencia` y `costoRegistrado` (el monto de este registro, no solo el acumulado), para que SE-6 reciba el movimiento y no tenga que calcular una diferencia.

### E-11 · EV-21, capa geográfica actualizada (SE-2)

**SAD, Cuadro 14:** SE-2 declara "Entrada de capas y zonas", pero el Cuadro 16 no tiene ningún evento para ese flujo.

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-21 | Capa geográfica actualizada | Adaptador de SE-2 | Evaluación de Daños, Consulta, Consulta de Daños, Gestión de Desastres | `capa (MUNICIPIOS / BARRIOS / ZONAS_RIESGO), versionCapa, divisiones[] {codigo, nombre, codigoPadre, geometria}, fuente` |

- **Drivers:** DA-07 (el municipio del activo), AD-17 (la caché de geometrías) y RF-GD-08 (la delimitación por municipios y barrios).
- **Nota:** el evento puede ser grande. Si una capa supera el tamaño de mensaje razonable del bus, unos pocos MB, se publica por partes, con `parte/total`, o el evento lleva un enlace al almacén de objetos. **No está en los documentos.**

### E-12 · Municipio del activo y de la evaluación

**SRS, Cuadro 9** (los 11 atributos de la evaluación). "Ubicación geográfica" queda igual, y se agrega un atributo derivado, que no se pide al evaluador: `codigoMunicipio`, calculado en el servidor a partir de la ubicación (DA-07).

**SRS, RF-ED-01:** agregar "El sistema deberá asignar a cada activo la división administrativa que contiene su ubicación".

**Pregunta abierta P-03:** si el activo puede ser una línea (vías y redes). Hoy el Cuadro 9 solo admite una ubicación, que se interpreta como un punto.

### E-13 · Dueño de la bandeja de revisión

- **SAD, Cuadro 16:** el consumidor de EV-10 es "Consulta de Daños (bandeja de revisión)". El Cuadro 15 pone `GET /v1/conflictos` y la resolución en Evaluación de Daños.
- **Propuesta:** la bandeja la lee y la resuelve **Evaluación de Daños**, porque resolver exige consistencia fuerte con la versión vigente (DA-09). Consulta de Daños consume EV-10 solo para marcar el activo en el mapa y para los indicadores de R-08. Se cambia el texto del consumidor a "Consulta de Daños (marca en el mapa e indicadores de revisión)".

### E-14 · Definición de "fusionar" (RF-ED-09)

**Agregar al SRS, RF-ED-09:** "Fusionar dos evaluaciones marcadas como posible duplicado significa elegir una como vigente y marcar la otra como duplicada de ella. La evaluación duplicada conserva todas sus versiones y sus evidencias. Sus evidencias quedan vinculadas también a la evaluación vigente. Ninguna versión ni evidencia se elimina."

**Driver:** el principio de AD-32 que AD-33 aplica a los duplicados: "La plataforma no decide qué versión de un dato prevalece [...] Ninguna versión se elimina".

### E-15 · EV-23, estado de integración cambiado (mitigación de R-10)

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-23 | Estado de integración cambiado | Cada adaptador | Consulta, Consulta del Programa, Consulta de Daños | `sistemaExterno, estadoAnterior, estadoNuevo (CERRADO / ABIERTO / SEMIABIERTO), instante, mensajesEnCola` |

- **ATAM, Cuadro 20, R-10:** cambiar el estado de "Abierto" a "Mitigado: EV-23", una vez implementado y verificado contra QA-01 (b): el indicador aparece en 30 s o menos.

### E-16 · EV-25, catálogo de tipos de activo actualizado

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-25 | Catálogo de tipos de activo actualizado | Evaluación de Daños | Consulta de Daños | `codigo, nombre, criterios[] {nivel, criterio}, version` |

**Driver:** QA-10 (0 redespliegues). Sin el evento, Consulta de Daños no conoce el nombre de un tipo nuevo.

### E-17 · Anexo B del SAD: tecnologías de la segunda entrega

Agregar un anexo con el mismo criterio que el Anexo A: "Estas tecnologías no forman parte de la arquitectura, que sigue expresada en mecanismos". El contenido es la tabla de la sección 7 del documento de revisión (DT-01 a DT-20), con la columna "Decisión del SAD que materializa".

**Por qué:** el enunciado (§8) dice "Esto se tendrá en cuenta para la segunda entrega", y CON-8 solo prohíbe nombrar productos en el cuerpo del diseño.

### E-18 · `secuenciaProductor` en el sobre canónico (mitigación de R-02)

- **SAD §5.2:** el sobre pasa de 8 a 9 campos, con `secuenciaProductor`: un entero consecutivo por productor, asignado en la bandeja de salida.
- **ATAM, Cuadro 20, R-02:** es la mitigación que el propio ATAM propone ("que cada productor numere sus eventos de forma consecutiva"). Cambiar el estado a "Mitigado" una vez implementado en Auditoría.

### E-19 · La bandeja de salida en todo productor

- **SAD, AD-45:** hoy aplica solo a EV-09 y EV-10 de Evaluación de Daños.
- **Propuesta:** "Todo microservicio que publica eventos de cambio los escribe en su bandeja de salida, en la misma transacción que el cambio".
  - **Drivers:** QA-04 (d) y QA-15 (b).
  - Es coherente con la mitigación de R-09: "La bandeja de salida (EA-19) aplica este principio".
- **SAD, Cuadro 5:** la fila "Bandeja de salida" pasa de "En el proceso de Evaluación de Daños" a "En cada microservicio productor".

### E-20 · Jurisdicción de la aprobación (Diseño RC)

- **Texto actual:** "Lo aprueba la autoridad con jurisdicción sobre el proyecto: local si está en un municipio, regional si abarca varios."
- **Texto propuesto:** "Lo aprueba la autoridad con jurisdicción sobre el territorio del proyecto: la autoridad local del municipio si el proyecto está en un solo municipio; la autoridad regional del departamento si abarca varios municipios de un mismo departamento; la autoridad nacional si abarca varios departamentos."

**Por qué:** el texto actual no exige que la autoridad sea la *de ese* territorio (H-32), y no le asigna ninguna función a la autoridad nacional, que sí aparece en el contexto (Figura 1). **La tercera regla no está en los documentos.** Ver P-02.

### E-21 · Alcance de la caché de geometrías (AD-17)

- **SAD, AD-17:** la caché pertenece a Consulta, y EV-05 la invalida.
- **Aclaración:** Consulta de Daños aplica el mismo mecanismo a las geometrías de las zonas de daño (las divisiones administrativas). Las invalidan EV-21 (una capa nueva) y los eventos de evaluación, para las respuestas de mapa ya armadas.
- **Vista física:** el "Motor de caché" atiende a las dos consultas, con un espacio de claves separado por servicio (AD-01).

### E-22 · Anexo A: simplificaciones superadas

El Anexo A (Cuadro 20) declara estas simplificaciones: "No hay API Gateway ni autenticación: el usuario, la organización y el dispositivo viajan en encabezados"; la visualización y Consulta de Daños quedan excluidas; "la publicación al bus" queda excluida; y las evidencias van "en disco".

**Propuesta:** agregar una nota que diga que en la segunda entrega estas simplificaciones se eliminan (DA-06, DA-08, DA-15 y DA-04), y que el Anexo A queda como registro del prototipo de verificación de QA-14.

### E-23 · Colas acotadas: política de desborde y mensajes envenenados

**SAD, AD-02 y Cuadro 5:** dicen "colas acotadas", pero no qué pasa cuando una cola llega a su límite, ni qué se hace con un mensaje que nunca se puede procesar.

**Aclaración propuesta:**

1. Al llegar al límite, la cola **rechaza la publicación**, y el evento permanece en la bandeja de salida del productor. Nunca se descarta un evento (QA-04).
2. Un mensaje que falla N veces se aparta a la cola de mensajes muertos del consumidor, con una alarma, y no bloquea los siguientes.
3. Un evento sin cola de destino se desvía a una cola de eventos sin ruta, con una alarma.

**Alternativa descartada:** descartar el mensaje más antiguo al llenarse, porque pierde eventos.

### E-24 · Mecanismo de conmutación (AD-26) y mitigación de R-09

**SAD, AD-26:** dice "conmutación automática", pero no qué la dispara ni en qué orden ocurre.

**Aclaración propuesta:**

1. El enrutador global sondea la región primaria. Tras N fallos consecutivos, desvía el tráfico y dispara el procedimiento de conmutación.
2. El procedimiento promueve las réplicas de datos de la región secundaria, escala sus servicios y activa sus publicadores.
3. Cada publicador de la región secundaria reenvía los eventos de su bandeja publicados durante el último intervalo igual al RPO más un margen. Los consumidores idempotentes descartan los duplicados.

**ATAM, R-09:** el paso 3 es una variante de la mitigación propuesta ("el productor reenvía el tramo pendiente"), y se apoya en que la bandeja viaja replicada con los datos. Cambiar el estado a "Mitigado" tras la prueba de F-10.

### E-25 · EV-27, organismos del desastre cambiados

**Ruta del SAD:** `/v1/desastres/{id}/organismos` asocia los organismos participantes y su responsabilidad. **Cuadro 16:** no tiene ningún evento para eso.

**Evento nuevo:**

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-27 | Organismos del desastre cambiados | Gestión de Desastres | Evaluación de Daños, Consulta | `idDesastre, organismoResponsable, participantes[] {organismo, responsabilidad}, version, autor` |

**Driver:** la regla de nivel 2 de la validación (RF-ED-08, cuyo actor es "Organismo responsable, autoridad"). Evaluación de Daños necesita saber qué organismos participan en el desastre, sin preguntárselo a Gestión de Desastres en cada validación (AD-01, CON-6).

### E-26 · Descarga filtrada e incremental

- **SAD, Cuadro 15:** `GET /v1/descarga` dice "activos de la zona", sin definir qué zona.
- **Aclaración propuesta:** "Acepta un desastre, una zona (código de división) y una versión desde la cual descargar solo los cambios. Sin zona, entrega los activos de la delimitación vigente del desastre. Solo incluye los desastres que no están cerrados."

### E-27 · Cifrado en tránsito entre servicios

- **SAD §7.1:** "Toda comunicación se cifra en tránsito, incluida la de los adaptadores con los sistemas externos."
- **Aclaración propuesta:** distinguir entre el borde (clientes, enrutador, gateway y sistemas externos: TLS obligatorio) y el tráfico interno del clúster (mTLS). Si el prototipo no implementa mTLS, se declara como límite. Ver P-06 del documento de revisión.

### E-28 · Origen de las candidatas en el Diseño RC

- **Diseño RC, Figura 1:** "Evaluación de Daños — evaluaciones validadas" ya es coherente con E-01.
- **CU-01:** agregar "a partir de su copia local de evaluaciones validadas, alimentada por los eventos de Evaluación de Daños".
- **Sección 5 (Entidades):** agregar las entidades de copia local `DesastreLocal` y `EvaluacionValidada`, que hoy existen en el código y no en el documento.

---

## Eventos del catálogo después de las enmiendas

| ID | Evento | Productor | Consumidores | Estado |
|---|---|---|---|---|
| EV-01 a EV-07 | (sin cambios) | | | EV-03 a 06 en alcance (Gestión de Desastres mínima) |
| EV-08 | Validación de evaluación cambiada | Evaluación de Daños | Consulta de Daños, **Reconstrucción** | E-01, E-02 |
| EV-09 | Evaluación sincronizada | Evaluación de Daños | Consulta de Daños | E-03 (campos) |
| EV-10 | Conflicto o duplicado detectado | Evaluación de Daños | Consulta de Daños (marca e indicadores) | E-13 |
| EV-11 a EV-17 | (sin cambios) | | | Fuera de alcance (Recursos) |
| EV-18 | Proyecto creado | Reconstrucción | Consulta del Programa, SE-6, SE-7, **Gestión de Desastres** | E-07, E-10 |
| EV-19 | Presupuesto cambiado | Reconstrucción | Consulta del Programa, SE-6 | E-10 |
| EV-20 | Estado, avance o datos del proyecto cambiados | Reconstrucción | Consulta del Programa, SE-7, **SE-6**, **Gestión de Desastres** | E-07, E-10 |
| **EV-21** | Capa geográfica actualizada | Adaptador SE-2 | Evaluación de Daños, Consulta, Consulta de Daños, Gestión de Desastres | E-11 |
| **EV-22** | Conflicto o duplicado resuelto | Evaluación de Daños | Consulta de Daños | E-05 |
| **EV-23** | Estado de integración cambiado | Adaptadores | Consulta, Consulta del Programa, Consulta de Daños | E-15 |
| **EV-24** | Avance o contratista reportado por SE-7 | Adaptador SE-7 | Reconstrucción | E-04 |
| **EV-25** | Catálogo de tipos de activo actualizado | Evaluación de Daños | Consulta de Daños | E-16 |
| **EV-26** | Evaluación actualizada en el servidor | Evaluación de Daños | Consulta de Daños | E-06 |
| **EV-27** | Organismos del desastre cambiados | Gestión de Desastres | Evaluación de Daños, Consulta | E-25 |

Auditoría consume todos los eventos de cambio, como ya dice el §5.2 del SAD.

Según E-29, Evaluación de Daños también consume EV-03, EV-05, EV-06, EV-21 y EV-27.

---

## Enmiendas que surgieron al implementar el paso 1

### E-29 · Evaluación de Daños consume los eventos de Gestión de Desastres

**SAD, Cuadro 16.** Agregar Evaluación de Daños como consumidor de EV-03, EV-05 y EV-06. EV-21 y EV-27 ya lo tenían en las enmiendas E-11 y E-25.

- **Por qué es correctiva:** RF-ED-06 exige asociar cada evaluación a un desastre registrado, y la descarga previa entrega "desastres activos" (Cuadro 15, `GET /v1/descarga`). Sin estos eventos, Evaluación de Daños no conoce los desastres sin consultar a Gestión de Desastres, lo que va contra AD-01 y CON-6.
- **Inconsistencia propia:** el flujo F-A de la revisión ya lo dibujaba, pero la tabla del catálogo de este archivo lo omitía. Ya está en la topología del bus (`q.evaluacion-danos`).

### E-30 · Campos adicionales de los eventos (aditivos)

Al escribir los esquemas hicieron falta estos campos, que el Cuadro 16 no tiene:

| Evento | Campos agregados | Para qué |
|---|---|---|
| EV-03 | `nombre`, `descripcion`, `instanteInicio`, `ubicacion`, `versionDesastre` | Las copias locales (Reconstrucción y Evaluación de Daños) muestran el desastre y lo ordenan por versión. `descripcion`, `instanteInicio` y `ubicacion` son atributos del Cuadro 7 del SRS. **`nombre` no está en el SRS**: es una etiqueta corta para mostrar |
| EV-06 | `versionDesastre` | Ordenar los cambios de estado en las copias locales sin depender del orden de llegada |
| EV-10 | `idActivo`, `tipoConflicto` | Que Consulta de Daños marque el activo en el mapa sin consultar a Evaluación de Daños |
| EV-18, EV-19, EV-20 | `proyecto` (el estado resultante: desastre, nombre, estado, presupuesto, costo, avance y versión) | Que los modelos de lectura (AD-15, AD-42) se actualicen solo con el evento, en cualquier orden, usando la versión |
| EV-19 | `tipoCambio`, `fuenteAnterior`, `fuenteNueva` | EV-19 cubre el presupuesto y la fuente (E-10) |

### E-31 · Cómo se asigna la secuencia del productor

**SAD §5.2 y AD-45:** agregar "La secuencia del productor la asigna el publicador de la bandeja de salida, bajo un contador por productor, solo a eventos ya confirmados. Así es consecutiva y no tiene huecos, aunque haya varias réplicas o una transacción se revierta."

- **Por qué:** la columna de identidad de la base, que era la candidata natural, consume números en las transacciones revertidas y no sigue el orden en que se confirman. Con ella, Auditoría detectaría huecos que no existen (R-02).
- **Detalle:** dentro de una transacción, los eventos se numeran en el orden de los hechos y no en el orden en que la base de datos inserta las filas.

### E-32 · El límite de entregas lo aplica el consumidor

**SAD, Cuadro 5,** fila "Consumo idempotente": agregar "Un mensaje que falla de forma transitoria se reintenta hasta un límite. Al superarlo, el consumidor lo aparta a la cola de mensajes muertos. Un mensaje que no cumple el contrato se aparta en el primer intento."

- **Por qué:** en el intermediario elegido (RabbitMQ 4), un rechazo con reencolado del protocolo que usan los servicios no cuenta para el límite de entregas de la cola. Si el límite quedara solo en la cola, un mensaje que siempre falla se reintentaría para siempre. Por eso el consumidor cuenta los intentos con el encabezado que pone el intermediario.
- **Verificado** en las pruebas de integración del paso 1.

## Enmiendas que surgieron en los pasos 2 y 3

### E-33 · EV-18 lleva el autor de la creación (aditivo)

**SAD, Cuadro 16, EV-18.** Agregar el campo `autor` (usuario que creó el proyecto).

- **Por qué es correctiva:** QA-13 exige reconstruir "quién, qué y cuándo" de cada cambio. EV-19 y EV-20 traen `autor`, pero EV-18 no, así que la creación de un proyecto quedaba en Auditoría sin actor.
- **Compatibilidad (R-03):** el campo es opcional en el esquema, para no romper mensajes ya emitidos; Reconstrucción lo envía siempre.

### E-34 · Auditoría registra también los tipos de evento que aún no conoce

**SAD, AD-21.** Agregar: "Auditoría valida el sobre de todo evento y los datos solo de los tipos cuyo esquema conoce. Un tipo nuevo se registra con el sobre completo, sin interpretar, en lugar de apartarse a la cola de mensajes muertos."

- **Por qué:** con la evolución aditiva del contrato (R-03), un productor puede publicar un tipo nuevo antes de que Auditoría se actualice. Si Auditoría lo rechazara, ese cambio quedaría sin registro, contra QA-15 (b): "0 cambios aplicados sin registro".
- Los demás consumidores siguen apartando lo que no cumple el contrato (E-32).

### E-35 · Rutas de configuración del ciclo de vida en Gestión de Desastres (aditivas)

**SAD, Cuadro 15, Gestión de Desastres.** Además de las rutas del cuadro, el servicio expone:

| Ruta | Para qué | Rol |
|---|---|---|
| `GET /v1/catalogos/tipos-desastre/{codigo}/ciclo-vida` | El perfil de ciclo de vida del tipo con sus transiciones (Cuadros 16 y 17) | Cualquier usuario autenticado |
| `GET`, `POST`, `PUT /v1/catalogos/perfiles-ciclo-vida` | Consultar y editar los perfiles en ejecución (RN-2, QA-10). Un perfil solo puede ganar estados mientras esté en uso | `administrador` para escribir |
| `GET`, `PUT /v1/catalogos/perfil-por-nivel` | El perfil mínimo de cada nivel de severidad (N1 Simplificado, N2 Estándar, N3 y N4 Extendido) | `administrador` para escribir |
| `GET`, `PUT /v1/desastres/{id}/organismos` | Organismo responsable y participantes (RF-GD-12, EV-27) | Solo el responsable para escribir (QA-11) |

- **Por qué:** RN-2 y RN-3 piden que el ciclo de vida dependa del tipo y del nivel y que se configure sin redespliegue (QA-10), pero el Cuadro 15 no tiene rutas para eso. **El perfil mínimo por nivel no está en los documentos**: lo decidió el usuario.

### E-36 · Reconstrucción consume EV-21 para mostrar el nombre del municipio

**SAD, Cuadro 16, EV-21.** Agregar Reconstrucción como consumidor.

- **Por qué:** EV-08 trae el código DIVIPOLA del municipio, pero las candidatas y los proyectos (CU-01, CU-02) se muestran con su nombre. Reconstrucción guarda solo el código y el nombre de cada municipio, sin la geometría. Hasta que llega la capa, muestra el código.
- **Topología:** `q.reconstruccion` se enlaza también a `ev21.#` (`despliegue/bus/generar_definiciones.py`).

### E-37 · Notificación: EV-07 en alcance y la cola de salida de SE-8 (EV-28 y EV-29)

**SAD, Cuadro 16.** EV-07 entra en alcance porque RF-GD-06 exige que toda reclasificación produzca una notificación, y RF-GD-15 pide registrar su estado de entrega. Se agregan dos eventos para el tramo Notificación ↔ adaptador de SE-8, que la vista de componentes de CU-1 dibuja como "cola de salida de SE-8" y "acuse de entrega", pero que el catálogo no tiene:

| ID | Evento | Productor | Consumidores | Campos |
|---|---|---|---|---|
| EV-07 | Notificación despachada | Notificación | Auditoría | Los del SAD (`idNotificacion, idEventoOrigen, organismosDestino, canal, estadoEntrega, intentos`) más `idDesastre, severidad, instanteAcuse` |
| EV-28 | Envío de notificación solicitado | Notificación | Adaptador de SE-8 (`q.salida.se8`) | `idNotificacion, intento, idEventoOrigen, idDesastre, organismo, canal, destino, asunto, mensaje, severidad` |
| EV-29 | Acuse de notificación | Adaptador de SE-8 | Notificación | `idNotificacion, intento, resultado (ENTREGADA o FALLIDA), motivo, instante, claveOrigen` |

- **Por qué dos eventos y no uno:** el Cuadro 16 dice que EV-07 lo produce Notificación. Si el adaptador publicara EV-07, Notificación no conservaría el estado de entrega (AD-13). Con EV-29, el adaptador solo informa el resultado de un intento y Notificación decide el estado.
- **Estados (RF-GD-15, QA-08):** PENDIENTE al crear la notificación y mientras SE-8 no acusa; ENTREGADA solo con acuse; FALLIDA al agotar los intentos. EV-07 se publica en cada paso a ENTREGADA o FALLIDA.
- **Topología:** `q.notificacion` (EV-03, EV-04 y EV-29) y `q.salida.se8` (EV-28), con los usuarios `notificacion` y `adaptador-se8`.

### E-38 · El marcador `desde` de la descarga es el instante del servidor, no una versión del catálogo

**Corrige DA-19 de la revisión** (`desde={versionCatalogo}`). Lo implementado en Evaluación de Daños y en la app es
`GET /v1/descarga?desastreId={id}&zona={divipola}&desde={instanteServidor}`: el cliente reenvía el `instanteServidor` de
su descarga anterior y recibe los activos que cambiaron después (con 60 s de traslape); el catálogo y los desastres van
completos, porque son pocos.

- **Por qué:** un instante sirve para todas las entidades sin un contador global de versiones del catálogo, y la
  respuesta dice si fue incremental (`incremental`, `desde`). El nombre del parámetro del desastre sigue siendo
  `desastreId`, el que ya usaba la app.
- La encontró la revisión de la app (Gemini) como contradicción entre DA-19 y el encargo.

### E-39 · La autoridad solo ve y usa los proyectos y las evaluaciones de su jurisdicción (Diseño RC, CU-01 a CU-11)

- **Texto actual:** el Diseño RC dice "Autoridad (local, regional o nacional): crea, asigna, aprueba y cancela proyectos"
  y, en CU-01, CU-10 y CU-11, que la autoridad consulta candidatas, historial y consolidado, sin decir de qué territorio.
- **Texto propuesto:** agregar a las reglas de negocio: "La autoridad solo ve y usa la información de su jurisdicción: la
  autoridad local, la de su municipio; la regional, la de los municipios de su departamento; la nacional, todo el
  territorio. Las candidatas, la lista y el detalle de proyectos, el historial y el consolidado se limitan a esa
  jurisdicción; un proyecto solo se crea con evaluaciones de municipios de ella, y solo se le asigna presupuesto o
  responsable, se aprueba o se cancela si es de ella. El auditor no tiene restricción territorial."
- **Fuente:** el Cuadro 3 del SRS ("Actores humanos de la plataforma"), columna "Qué información puede ver": la autoridad
  local ve "la información de su jurisdicción, incluidos recursos, personas afectadas y proyectos"; la regional, "la
  información de los municipios de su región"; la nacional, "todos los desastres, en todo el territorio".
- **Cómo se aplica (DA-14):** el territorio es el código DIVIPOLA de la autoridad (5 dígitos el municipio, 2 el
  departamento). Un proyecto es de la jurisdicción si alguno de sus municipios lo es; una evaluación, si su municipio lo
  es. Fuera de la jurisdicción, el detalle, el historial y las acciones de la autoridad (asignar presupuesto, fuente y
  cronograma; asignar responsable; aprobar; cancelar) responden **404**, igual que un proyecto que no existe, para no
  revelar que existe. Esa revisión va antes que cualquier otra regla: un dato inválido o una transición imposible
  tampoco se le dicen a quien no ve el proyecto. Un lote con alguna evaluación ajena se rechaza entero con **403**: ahí
  la autoridad pide crear con evaluaciones, no pregunta por un proyecto.
- **Aprobar: 404 o 403.** Quien no ve el proyecto recibe 404 (Calarcá con un proyecto de Armenia). Quien sí lo ve pero
  no es la autoridad que lo aprueba según E-20 recibe **403** (la gobernación del Quindío con un proyecto municipal de
  Armenia, o la nacional con uno regional). En la primera versión de DA-14 Calarcá recibía 403; se cambió a 404 porque
  un 403 le confirmaba que el proyecto existe.
- **Alternativa descartada:** responder 403 en el detalle, el historial y las acciones sobre un proyecto ajeno. Confirma
  que el proyecto existe a quien no tiene por qué saberlo.
- **Costo aceptado:** una autoridad no ve proyectos de su región que no tocan su municipio, aunque compartan desastre;
  para eso está la autoridad regional.

### E-40 · Evolución por zona en Consulta de Daños (DA-08, RF-ED-15)

- **Texto actual:** RF-ED-15 pide "para un activo o una zona, la secuencia de evaluaciones registradas en el tiempo con
  su nivel de daño". DA-08 solo definió `GET /v1/consulta-danos/activos/{id}/evolucion`.
- **Texto propuesto:** agregar a DA-08 la ruta `GET /v1/consulta-danos/zonas/{codigo}/evolucion?desastre=`, para cualquier
  usuario autenticado. `codigo` es el DIVIPOLA del municipio (5 dígitos) o del departamento (2). La respuesta trae la zona
  (código, nombre y tipo), `datoAl`, la secuencia ordenada por instante y, por día, la cantidad de activos por nivel.
  Responde 400 si el código o el desastre no son válidos y 404 si la zona no está en la capa o no tiene evaluaciones.
- **Tipo:** aditiva. No cambia ninguna ruta, evento ni proyección existente: lee `p_historial_activo` y `p_municipios`.
- **Cómo se aplica:**
  - La zona existe si está en la capa EV-21. El departamento de cada entrada se resuelve como en el mapa: el de la capa
    y, si el municipio aún no está en ella, los dos primeros dígitos de su código.
  - `datoAl` es el instante de la última entrada devuelta (AD-16): con filtro, la última de ese desastre.
  - En el agregado, el día es el de Colombia (UTC-5, sin horario de verano). Cada activo con entradas ese día cuenta una vez, en el último nivel que tuvo ese día.
    Una evaluación rechazada hasta ese día no cuenta, como en el mapa, pero sigue en la secuencia.
  - EV-21 no trae el nombre del departamento, así que sale de una tabla fija en el dominio, con los 33 nombres de
    `datos/geo/municipios-colombia.geojson`. La existencia del departamento se sigue validando contra la capa.
- **Alternativas descartadas:** acumular por día el estado de todos los activos de la zona, porque RF-ED-15 pide las
  evaluaciones registradas y el estado acumulado ya lo dan el mapa y el consolidado; y agregar el nombre del departamento
  a EV-21 y a `p_municipios`, porque cambia un contrato y una proyección para un dato que casi no cambia.
- **Costo aceptado:** `p_historial_activo` no tiene índice por municipio, así que cada consulta recorre la tabla. Si el
  historial crece hasta notarse, el arreglo es un índice por `codigo_municipio` e `instante`, con su migración.

### E-41 · Tablero de daños en Razor Pages: excepción a la consola única (DA-13)

- **Texto actual:** DA-13 define una sola consola de coordinación en Angular para los actores de la plataforma.
- **Texto propuesto:** agregar a DA-13: "Por requisito del curso, el tablero de daños (`/tablero-danos`) se implementa con
  Razor Pages de ASP.NET Core, dentro de Consulta de Daños. La consola lo enlaza desde su menú."
- **Tipo:** excepción acotada y aditiva. Las pantallas de la consola y las rutas de la API no cambian.
- **Por qué no rompe la arquitectura:** las páginas son un segundo adaptador de entrada de `ConsultaDanos.Api` y llaman en
  el mismo proceso a `ConsultarConsolidado` y `ConsultarIndicadores`, los mismos casos de uso de la API. Dominio y Aplicación
  no cambian (AD-08).
- **Cómo se aplica:**
  - Inicio de sesión OpenID Connect con PKCE y cookie, con el cliente confidencial `consulta-danos-tablero`. Ven el tablero
    `autoridad`, `organismo`, `operador` y `auditor`.
  - El catálogo del gateway (DA-12) agrega `cd-tablero`: `GET /tablero-danos/{**resto}`, `roles: publico`,
    `reglaDominio: ninguna`. Es pública para el gateway porque el navegador trae cookie y no un `Bearer`; la autorización la
    hace la página.
  - El catálogo no admite rutas públicas de modificación (R-07). Por eso no se agrega un `POST` para el retorno de OIDC: el
    código de autorización regresa por `GET` (`response_mode=query`), protegido con PKCE, `state` y `nonce`.
  - Consulta de Daños no guarda el nombre de los desastres. El selector lo lee del servicio Consulta por la red interna, con
    la cuenta de servicio del cliente; si no responde, el tablero avisa y sigue atendiendo el desastre de la dirección.
- **Alternativas descartadas:**
  - Otra pantalla Angular: no cumple el requisito del curso.
  - Un proyecto web aparte que llame por HTTP a la API: duplica un despliegue y no demuestra dos interfaces sobre los mismos
    casos de uso.
  - Permitir `POST` público en el catálogo: debilita una regla de seguridad para un caso que `GET` resuelve.

### E-42 · Registro del desastre en campo desde el cliente móvil (RF-GD-02, AD-48)

- **Texto actual:** RF-GD-02 asigna el registro manual al organismo de respuesta y a la autoridad, sin decir desde dónde.
  El Cuadro 15 define `POST /v1/desastres` sin identificador del cliente.
- **Texto propuesto (SRS):** "El sistema deberá permitir que un usuario autorizado registre un evento de desastre indicando
  los atributos mínimos del Cuadro 7, desde la consola o desde el cliente móvil en campo, aun sin conexión."
- **Texto propuesto (SAD, Cuadro 15):** "`POST /v1/desastres` acepta un `id` opcional generado por el cliente. El reenvío del
  mismo `id` por el mismo autor devuelve el desastre ya registrado; si el `id` pertenece a otro autor responde 409
  `ID_EXISTE`." Nueva decisión AD-48.
- **Tipo:** aditiva. El registro desde la consola no cambia.
- **Implementación:** `servicios/gestion-desastres` (`RegistrarDesastre`, prueba
  `El_reenvio_con_el_mismo_id_desde_campo_no_duplica_el_desastre`) y repositorio `danos` (pantallas Desastres y Registrar
  desastre, tabla Room `registro_desastre`, `EnviadorDesastres`, `ContratoServidorTest`).

### E-43 · El Proveedor de identidad en las vistas (DA-15, AD-49)

- **Texto actual:** las vistas lógica y física del SAD no tienen un elemento para la identidad federada, aunque DA-15, AD-04
  y AD-22 la usan.
- **Texto propuesto:** agregar el componente externo "Proveedor de identidad" (OIDC con PKCE) en la vista lógica, su nodo en
  la vista física y la decisión AD-49: el token se valida en el gateway (nivel 1) y en cada microservicio (nivel 2).
- **Tipo:** aditiva. La tecnología (Keycloak 26) va en AD-58.

### E-44 · CON-9: Jakarta EE y .NET en la solución final; adaptador de SE-8 en Jakarta EE (AD-66)

- **Origen:** restricción que agregó el profesor en la entrega 2: "Se debe incluir JEE y
  .NET en la solución final". Hasta ese día no había nada en Java ni en Jakarta EE.
- **Texto propuesto (SRS 1.2):** CON-9 "La solución final debe incluir Jakarta EE y .NET". Se cumple en la frontera: el
  adaptador de SE-8 en Jakarta EE y el núcleo en .NET. No cambia ningún requisito funcional ni escenario. Fila en la Matriz
  4; QA-08 parcial y QA-09 cumplido en la Matriz 5; límite L-11.
- **Texto propuesto (SAD 1.2):** decisión de tecnología **AD-66 "Plataforma del adaptador SE-8"**: Jakarta EE 11 sobre
  WildFly 42.0.0.Final (Java 21), frente a Spring Boot 3 y .NET 10, con el criterio "cumplimiento de CON-9" en la matriz
  (4,35 frente a 4,00 de .NET y 3,65 de Spring Boot). AD-50 agrega: "El núcleo sigue en .NET aun con CON-9: Jakarta EE se
  confina a la frontera según AD-03 y AD-66". SE-8 pasa a «implementado» en el alcance, en las cuatro vistas, en el modelo
  de integración, en los catálogos de eventos y colas y en el Cuadro 15, con la evidencia del `git diff --stat`.
- **Texto propuesto (ATAM 1.2):** la medida "0 componentes modificados" de QA-09 tiene evidencia implementada. Riesgo
  nuevo **R-15**: dos pilas que mantener en la frontera.
- **Límite L-11 (sustituto temporal de Notificación):** sin Notificación, nadie publicaba EV-28 y `q.salida.se8` no
  recibía nada. Se agregan los enlaces `ev03.#` y `ev04.#` a `q.salida.se8` en `definiciones.json` y en
  `generar_definiciones.py`. El adaptador, con `Sustituto__Activo=true`, alerta a un único destino de difusión y no publica
  EV-29 (nadie lo consumiría). Se retira quitando los dos enlaces y apagando la variable.
- **Tipo:** aditiva. No se agrega ningún evento al catálogo.
- **Alternativas descartadas:** implementar el adaptador en .NET (no cumple CON-9) o en Spring Boot 3 (no es la plataforma
  Jakarta EE); escribir un microservicio de núcleo en Java (rompe AD-50 y la reutilización de `comun`); implementar
  Notificación completa el día de la entrega (fuera de plazo; queda en L-06).
- **Costo aceptado:** segunda pila en la frontera (R-15). Dos piezas propias en lugar del estándar: un cortacircuitos con la
  ventana de 30 s y el aviso de apertura para EV-23 (MicroProfile Fault Tolerance no los da) y un EJB `@Singleton` en lugar
  de MDB (RabbitMQ no tiene resource adapter JCA mantenido).
- **Implementación:** `adaptadores/adaptador-se8-jee`, `simuladores/sim-se8`, `pruebas/humo/prueba-se8.sh`,
  `despliegue/k8s/base/adaptador-se8.yaml` y los servicios `adaptador-se8`, `adaptador-se8-migrador` y `sim-se8` del
  compose. Ningún microservicio, proyecto `Plataforma.Comun.*`, gateway ni adaptador existente cambió.
