package plataforma.adaptador.se8.jee;

import jakarta.ejb.LocalBean;
import jakarta.ejb.Stateless;
import jakarta.ejb.TransactionAttribute;
import jakarta.ejb.TransactionAttributeType;
import jakarta.inject.Inject;
import java.time.Clock;
import java.util.List;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.cliente.TransporteSe8;
import plataforma.adaptador.se8.configuracion.Configuracion;
import plataforma.adaptador.se8.contrato.ContratoAdaptador;
import plataforma.adaptador.se8.contrato.ResultadoEntrega;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.contrato.ValidadorContrato;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.AlertaSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/**
 * Contrato del adaptador de SE-8 (DA-11, AD-09): EJB sin estado que implementa el equivalente Java de
 * {@code IContratoAdaptador}. Salida: valida el sobre contra el contrato, lo traduce y lo entrega a SE-8 con la
 * resiliencia del Cuadro 11. Entrada: los acuses de SE-8 desde una marca de avance, traducidos a EV-29.
 *
 * <p>Participa en la transacción de quien lo llama ({@code SUPPORTS}): el EJB que aplica el mensaje es el que la abre.</p>
 */
@Stateless
@LocalBean
@TransactionAttribute(TransactionAttributeType.SUPPORTS)
public class AdaptadorSe8 implements ContratoAdaptador<AcuseSe8> {

    @Inject
    ClienteSe8 cliente;
    @Inject
    OpcionesSustituto sustituto;
    @Inject
    Configuracion configuracion;
    @Inject
    Clock reloj;

    private final ValidadorContrato validador = ValidadorContrato.instancia();

    public AdaptadorSe8() {
        // para el contenedor
    }

    /** Para las pruebas, fuera del servidor. */
    public AdaptadorSe8(ClienteSe8 cliente, OpcionesSustituto sustituto, Configuracion configuracion, Clock reloj) {
        this.cliente = cliente;
        this.sustituto = sustituto;
        this.configuracion = configuracion;
        this.reloj = reloj;
    }

    @Override
    public String sistemaExterno() {
        return TraduccionSe8.SISTEMA;
    }

    @Override
    public ResultadoEntrega entregar(Sobre eventoCanonico) {
        validador.validar(eventoCanonico);
        AlertaSe8 alerta = TraduccionSe8.aSe8(eventoCanonico, sustituto);
        if (alerta == null) {
            return ResultadoEntrega.OMITIDA;
        }
        return cliente.enviar(alerta);
    }

    @Override
    public Lote<AcuseSe8> recuperarDesde(String marcaAvance) {
        long desde = marcaAvance == null || marcaAvance.isBlank() ? 0 : Long.parseLong(marcaAvance);
        TransporteSe8.PaginaAcuses pagina = cliente.acusesDesde(desde, configuracion.tamanoPagina());
        List<AcuseSe8> acuses = pagina.acuses();
        String siguiente = acuses.isEmpty() ? null : Long.toString(acuses.get(acuses.size() - 1).secuencia());
        return new Lote<>(acuses, siguiente, pagina.hayMas());
    }

    @Override
    public Sobre traducir(AcuseSe8 externo) {
        return TraduccionSe8.aEvento(externo, reloj.instant());
    }
}
