package plataforma.adaptador.se8.nucleo;

import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.UUID;
import plataforma.adaptador.se8.contrato.CatalogoEventos;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.EstadoIntegracionFila;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/**
 * EV-23 (E-15, QA-01 b): la apertura y el cierre del cortacircuitos de SE-8. El cambio de {@code estado_integracion} y el
 * EV-23 en la bandeja se confirman en la misma transacción (AD-45). Clave {@code integracion:SE-8:<id de la transición>},
 * como {@code TraduccionEstadoIntegracion.Clave} de comun.
 */
public final class EstadoIntegracionSe8 {

    private final AlmacenSe8 almacen;

    public EstadoIntegracionSe8(AlmacenSe8 almacen) {
        this.almacen = almacen;
    }

    /** Registra la transición. Devuelve falso si el estado ya era ese (otra réplica o un aviso repetido). */
    public boolean registrar(UUID idTransicion, String nuevo, Instant instante, Instant abiertoDesde, long mensajesEnCola) {
        EstadoIntegracionFila fila = almacen.estadoIntegracion(TraduccionSe8.SISTEMA);
        String anterior = fila.cambiar(nuevo, "ABIERTO".equals(nuevo) ? abiertoDesde : null, instante);
        if (anterior.equals(nuevo)) {
            return false;
        }
        almacen.agregarABandeja(ev23(idTransicion, anterior, nuevo, instante, mensajesEnCola));
        return true;
    }

    public static Sobre ev23(UUID idTransicion, String anterior, String nuevo, Instant instante, long mensajesEnCola) {
        Map<String, Object> datos = new LinkedHashMap<>();
        datos.put("sistemaExterno", TraduccionSe8.SISTEMA);
        datos.put("estadoAnterior", anterior);
        datos.put("estadoNuevo", nuevo);
        datos.put("instante", instante.toString());
        datos.put("mensajesEnCola", Math.max(0, mensajesEnCola));
        return Sobre.crear(CatalogoEventos.ESTADO_INTEGRACION_CAMBIADO, CatalogoEventos.ADAPTADOR_SE8,
                "integracion:" + TraduccionSe8.SISTEMA + ":" + idTransicion, datos, instante, null, null);
    }
}
