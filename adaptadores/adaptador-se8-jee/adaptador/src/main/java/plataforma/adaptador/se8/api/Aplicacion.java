package plataforma.adaptador.se8.api;

import jakarta.ws.rs.ApplicationPath;
import jakarta.ws.rs.core.Application;

/**
 * API interna del adaptador (Jakarta REST). Solo en la red interna del compose y del clúster: el API Gateway no tiene
 * rutas hacia el adaptador. La usan SE-8 (aviso de acuse), los sondeos del orquestador y la prueba de humo.
 */
@ApplicationPath("/")
public class Aplicacion extends Application {
}
