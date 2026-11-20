# Adaptador de SE-8 (comunicación y notificación) en Jakarta EE

Integra la plataforma con SE-8 (Cuadro 14 del SAD). Es el único componente en Java: cumple **CON-9** ("la solución
final debe incluir Jakarta EE y .NET") en la frontera, donde AD-03 ubica la heterogeneidad tecnológica. El núcleo sigue
en .NET (AD-50). Decisión: **AD-66** del SAD. Incorporarlo modificó **0 componentes existentes** (QA-09, Cuadro 15):
solo se agregaron archivos, más dos enlaces de bus y la configuración de despliegue.

| | |
|---|---|
| Servidor | WildFly 42.0.0.Final, imagen `quay.io/wildfly/wildfly:42.0.0.Final-jdk21` |
| Plataforma | Jakarta EE 11, Java 21, un solo WAR (`adaptador-se8.war`, publicado en `/`) |
| Base | `adaptador_se8` en la instancia `db`, esquema `se8`, rol `adaptador_se8_app` (SELECT, INSERT, UPDATE) |
| Bus | Usuario `adaptador-se8`: lee `q.salida.se8`, escribe en `plataforma.eventos` |
| Contrato | Los esquemas de `comun/Plataforma.Comun.Eventos/Esquemas` (se copian al WAR al compilar, no se duplican) |

## Especificaciones Jakarta EE que usa

| Pieza | Especificación | Clase |
|---|---|---|
| Consumo de `q.salida.se8` | Jakarta Messaging 3.1 (cliente JMS de RabbitMQ, destino AMQP sobre la cola existente) + Jakarta Concurrency (`ManagedExecutorService`) + EJB `@Singleton @Startup` | `consumo/BucleConsumo`, `jee/ConsumidorSe8` |
| Contrato del adaptador | EJB `@Stateless` que implementa `ContratoAdaptador` (equivalente de `IContratoAdaptador`) | `jee/AdaptadorSe8`, `contrato/ContratoAdaptador` |
| Unidades de trabajo | EJB `@Stateless` con `@TransactionAttribute(REQUIRES_NEW)` (JTA) | `jee/ManejadorSe8` |
| Estado de entrega, marca de avance, bandeja | Jakarta Persistence 3.2 (Hibernate de WildFly) | `persistencia/*` |
| Relevo de la bandeja y reenvío | EJB Timer Service (`@Schedule` y temporizador de intervalo) | `jee/Temporizadores` |
| Llamadas a SE-8 | Jakarta REST Client (JAX-RS) | `cliente/TransporteJaxRs` |
| Acuse, salud y consulta | Jakarta REST (JAX-RS) | `api/*` |
| Productores | CDI 4.1 | `jee/Produccion` |
| Configuración | MicroProfile Config (variables de entorno, mismos nombres que .NET) | `configuracion/Configuracion` |

Dos piezas propias, explicadas en AD-66 ("Compromiso aceptado"):
- **EJB `@Singleton` en lugar de MDB.** Un MDB necesita un resource adapter JCA para el proveedor de mensajería, y
  RabbitMQ no tiene uno mantenido. El consumo usa igual la API estándar (`Connection`, `Session` transaccional,
  `MessageConsumer`). Lo único propio es quién arranca el hilo.
- **Cortacircuitos propio en lugar de MicroProfile Fault Tolerance.** `@CircuitBreaker` mide una ventana de N llamadas,
  no la ventana de 30 s del Cuadro 10, y no avisa la apertura, que hace falta para publicar EV-23.

## Qué hace

1. **Salida.** Consume `q.salida.se8` y hace con cada mensaje lo mismo que `ConsumidorIdempotente` de `comun`:
   - Valida el sobre contra `sobre.v1.json` y el `EV-nn.v1.json` del tipo. Si no cumple, va directo a `dlq.salida.se8`.
   - Lo aplica en una transacción JPA y confirma el mensaje JMS **solo después del commit** (`session.commit()` = ack).
   - Es idempotente por `claveIdempotencia`: llave primaria en `mensajes_procesados`.
   - Un fallo de aplicación suma en `intentos_consumo`. Al quinto, `rollback()` con `nackOnRollback` aparta el mensaje
     a la DLQ. Antes de eso, cerrar la sesión lo reencola. Sin base, cuenta `JMSXDeliveryCount`.
2. **Traducción.** EV-28 (de Notificación) da una alerta por destinatario e intento, con clave
   `notificacion:<id>:<intento>`. En **modo sustituto** (L-11), EV-03 y EV-04 dan una alerta al destino de difusión con
   el ámbito del nivel (SRS Cuadro 18), con clave `sustituto:<claveIdempotencia>`.
3. **Entrega a SE-8.** `PUT /v1/alertas/{referencia}` (idempotente). Resiliencia del Cuadro 10: 3 s por intento,
   20 s por operación y 2 reintentos con espera exponencial desde 0,5 s. Se reintentan solo operaciones idempotentes.
   El cortacircuitos se abre con 3 o más llamadas en 30 s y 100 % de fallo, y queda 15 s abierto antes de la prueba.
   Con el circuito abierto, el consumo se pausa: los mensajes esperan en la cola y nada va a la DLQ.
4. **Estado de entrega (QA-08).** `PENDIENTE → ENVIADA` (SE-8 la recibió) `→ ENTREGADA` solo con el acuse. Un rechazo
   de SE-8 o los envíos agotados la dejan `FALLIDA`. La restricción `ck_entregada_con_acuse` de la base impide una
   entrega sin acuse.
5. **Acuse.** SE-8 avisa con `POST /v1/acuses`, por una ruta solo de la red interna (el gateway no la enruta). Cada
   30 s, y enseguida después de cerrarse el circuito, el adaptador concilia `GET /v1/acuses?desde=<marca>` desde su
   marca de avance (AD-14).
6. **Reenvío por falta de acuse.** Es un mecanismo aparte de la resiliencia. Una alerta del modo sustituto sin acuse
   en 5 s vuelve a `PENDIENTE` y se reenvía con la misma referencia, y al quinto envío queda `FALLIDA`. Con EV-28,
   el reintento lo decide Notificación (AD-13).
7. **Eventos que publica, siempre por la bandeja** (misma transacción, secuencia del productor, AD-45):
   - **EV-23**: al abrirse y al cerrarse el circuito.
   - **EV-29**: con el acuse o el rechazo, solo para EV-28.

## Configuración (variables de entorno)

| Variable | Uso (valor por omisión) |
|---|---|
| `ConnectionStrings__AdaptadorSe8` | Formato .NET (`Host=db;Port=5432;Database=adaptador_se8;Username=adaptador_se8_app;Password=...`); `wildfly/entrada.sh` la traduce al datasource |
| `Bus__Nodos__0..n`, `Bus__Usuario`, `Bus__Clave`, `Bus__Productor` | Bus (`adaptador-se8`) |
| `Bus__Cola`, `Bus__LimiteEntregas` | `q.salida.se8`, 5 |
| `Se8__Url` | `http://sim-se8:8080/` |
| `Resiliencia__*` | Mismos nombres que .NET: `TiempoEsperaIntentoSegundos` (3), `TiempoEsperaTotalSegundos` (20), `Reintentos` (2), `EsperaBaseReintentoSegundos` (0.5), `MinimoLlamadas` (3), `ProporcionFallos` (1.0), `VentanaSegundos` (30), `DuracionAperturaSegundos` (15) |
| `Salida__EsperaReintentoSegundos`, `Salida__IntervaloSondeoSegundos` | 2, 5 |
| `Reenvio__SegundosSinAcuse`, `Reenvio__MaximoEnvios` | 5, 5 |
| `Entrada__IntervaloSegundos`, `Entrada__TamanoPagina` | 30, 100 |
| `Sustituto__Activo`, `Sustituto__Canal`, `Sustituto__Destino`, `Sustituto__Organismo` | `true`, `CORREO`, `sala-crisis@ungrd.gov.co`, `Sala de crisis (difusión)` |
| `ConnectionStrings__AdaptadorSe8Dueno`, `Migracion__ClaveAdaptadorSe8App` | Solo el migrador |

**Retirar el modo sustituto** cuando exista Notificación: quitar `"ev03"` y `"ev04"` de `q.salida.se8` en
`despliegue/bus/generar_definiciones.py`, regenerar `definiciones.json` y poner `Sustituto__Activo=false`.

## API interna (red del compose o del clúster; sin ruta en el gateway)

| Ruta | Uso |
|---|---|
| `GET /salud/vivo`, `GET /salud/listo` | Sondeos (listo: base y consumidor) |
| `POST /v1/acuses` | Aviso de acuse de SE-8 (200 aplicado o repetido, 404 alerta aún desconocida) |
| `GET /v1/entregas?estado=&desastre=&limite=` | Estado de entrega por destinatario (QA-08) |
| `GET /v1/entregas/{claveEnvio}` | Una entrega |
| `GET /v1/integracion` | Circuito, consumo, entregas por estado, EV-23 publicados |

## Compilar, ejecutar y probar

```bash
mvn -f adaptadores/adaptador-se8-jee/pom.xml test      # 52 pruebas unitarias (JUnit 5 + Mockito), sin Docker
mvn -f adaptadores/adaptador-se8-jee/pom.xml verify    # además 7 de integración (Testcontainers: RabbitMQ 4.4 con definiciones.json y PostgreSQL 18)
docker compose -f despliegue/compose/docker-compose.yml up -d --build adaptador-se8   # migrador, simulador y adaptador
bash pruebas/humo/prueba-se8.sh                        # prueba de humo de punta a punta (QA-08, QA-09)
```

Las pruebas de integración usan la topología real del bus: cola quorum, política con `delivery-limit` 5, DLX y el
usuario `adaptador-se8` de mínimo privilegio. También usan la base que crea el migrador, a la que el adaptador entra
con su rol. Demuestran:
- el ack solo después del commit JPA (el mensaje está sin confirmar durante la transacción y vuelve si el commit falla);
- la quinta entrega fallida que va a la DLQ (contada en la base y, sin base, con `JMSXDeliveryCount`);
- el contrato inválido que va directo a la DLQ;
- el duplicado que no se reenvía;
- con SE-8 caído: el circuito se abre, EV-23 llega a `q.consulta`, la cola se conserva y al volver se entrega una sola
  vez, con la secuencia sin huecos.

## Estructura

```
adaptador-se8-jee/
  pom.xml                     padre (Java 21)
  migrador/                   JAR con Flyway: base, rol mínimo y db/migration/V1__inicial.sql
  adaptador/                  WAR
    contrato/                 Sobre, CatalogoEventos, ValidadorContrato, ContratoAdaptador
    traduccion/               AlertaSe8, AcuseSe8, TraduccionSe8 (sin base, bus ni HTTP)
    resiliencia/              Cortacircuitos, EjecutorResiliente, OpcionesResiliencia
    cliente/                  TransporteJaxRs, ClienteSe8
    persistencia/             entidades JPA y AlmacenJpa
    nucleo/                   ProcesadorSe8, AplicadorAcuses, ConciliadorAcuses, ReenvioSinAcuse, RelevoBandeja, EstadoIntegracionSe8
    consumo/                  BucleConsumo, ConexionesBus, PublicadorJms, ContadorCola
    jee/                      EJB y productores CDI
    api/                      recursos JAX-RS
  wildfly/                    configurar.cli (datasource), module.xml (PostgreSQL), entrada.sh
  Dockerfile                  etapas: compilacion, migrador, adaptador
```
