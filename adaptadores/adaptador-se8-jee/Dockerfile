# Adaptador de SE-8 en Jakarta EE 11 sobre WildFly 42 (AD-66). Una imagen por etapa:
#   docker build -f adaptadores/adaptador-se8-jee/Dockerfile --target migrador -t plataforma/adaptador-se8-migrador:local .
#   docker build -f adaptadores/adaptador-se8-jee/Dockerfile -t plataforma/adaptador-se8:local .
# Se construye desde la raíz del monorepo: el WAR toma los esquemas del contrato de comun/Plataforma.Comun.Eventos.

FROM maven:3.9.16-eclipse-temurin-21 AS compilacion
WORKDIR /fuente
COPY comun/Plataforma.Comun.Eventos/Esquemas/ comun/Plataforma.Comun.Eventos/Esquemas/
COPY adaptadores/adaptador-se8-jee/ adaptadores/adaptador-se8-jee/
# Las pruebas corren fuera de la imagen (las de integración necesitan Docker): mvn verify, ver el README.
RUN --mount=type=cache,target=/root/.m2 \
    mvn -B -q -f adaptadores/adaptador-se8-jee/pom.xml -Dmaven.test.skip=true package \
 && mvn -B -q -f adaptadores/adaptador-se8-jee/adaptador/pom.xml dependency:copy \
        -Dartifact=org.postgresql:postgresql:42.7.14 -DoutputDirectory=/controladores

# Migrador: crea la base adaptador_se8, el rol adaptador_se8_app y aplica las migraciones con Flyway, y termina.
FROM eclipse-temurin:21-jre AS migrador
WORKDIR /app
COPY --from=compilacion /fuente/adaptadores/adaptador-se8-jee/migrador/target/migrador-se8.jar .
USER 1001
ENTRYPOINT ["java", "-jar", "/app/migrador-se8.jar"]

# Adaptador: un solo despliegue (WAR) en WildFly 42.0.0.Final, Jakarta EE 11, Java 21.
FROM quay.io/wildfly/wildfly:42.0.0.Final-jdk21 AS adaptador
COPY --chown=jboss:jboss adaptadores/adaptador-se8-jee/wildfly/module.xml /opt/jboss/wildfly/modules/org/postgresql/main/module.xml
COPY --chown=jboss:jboss --from=compilacion /controladores/postgresql-42.7.14.jar /opt/jboss/wildfly/modules/org/postgresql/main/postgresql.jar
COPY --chown=jboss:jboss adaptadores/adaptador-se8-jee/wildfly/configurar.cli /tmp/configurar.cli
RUN /opt/jboss/wildfly/bin/jboss-cli.sh --file=/tmp/configurar.cli \
 && rm -rf /opt/jboss/wildfly/standalone/configuration/standalone_xml_history /opt/jboss/wildfly/standalone/data \
           /opt/jboss/wildfly/standalone/log /tmp/configurar.cli
COPY --chown=jboss:jboss adaptadores/adaptador-se8-jee/wildfly/entrada.sh /opt/jboss/entrada.sh
COPY --chown=jboss:jboss --from=compilacion /fuente/adaptadores/adaptador-se8-jee/adaptador/target/adaptador-se8.war \
     /opt/jboss/wildfly/standalone/deployments/adaptador-se8.war
USER root
RUN chmod 755 /opt/jboss/entrada.sh
USER jboss
ENV JAVA_OPTS="-Xms128m -Xmx384m -Djava.net.preferIPv4Stack=true -Duser.timezone=UTC"
EXPOSE 8080
ENTRYPOINT ["/opt/jboss/entrada.sh"]
