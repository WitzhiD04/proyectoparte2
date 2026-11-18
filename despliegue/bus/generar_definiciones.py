#!/usr/bin/env python3
"""
Genera despliegue/bus/definiciones.json: la topología del bus declarada al desplegar (DA-03).

Una sola fuente para el exchange de la plataforma, las colas por consumidor, sus enlaces, las políticas de cola
acotada y de mensajes muertos, y los usuarios con mínimo privilegio. RabbitMQ carga el archivo al arrancar
(definitions.local.path en rabbitmq.conf). Los servicios solo verifican la topología (declaración pasiva).

Uso:  python despliegue/bus/generar_definiciones.py
Las claves salen de las variables BUS_CLAVE_<USUARIO> (por ejemplo BUS_CLAVE_RECONSTRUCCION). Sin variable se usa
"<usuario>_local", que es solo para desarrollo (aviso A-15 del plan del paso 1).
"""
import base64
import hashlib
import json
import os
from pathlib import Path

EXCHANGE = "plataforma.eventos"
SIN_RUTA = "plataforma.sin-ruta"
DLX = "plataforma.dlx"

# Cola por consumidor y los eventos que recibe (ENMIENDAS_DOCUMENTOS.md, catálogo después de las enmiendas, y E-29).
COLAS = {
    "q.reconstruccion": ["ev03", "ev06", "ev08", "ev21", "ev24"],
    "q.consulta-programa": ["ev18", "ev19", "ev20", "ev23"],
    "q.consulta-danos": ["ev08", "ev09", "ev10", "ev21", "ev22", "ev23", "ev25", "ev26"],
    "q.consulta": ["ev03", "ev04", "ev05", "ev06", "ev21", "ev23", "ev27"],
    "q.gestion-desastres": ["ev18", "ev20", "ev21"],
    "q.evaluacion-danos": ["ev03", "ev05", "ev06", "ev21", "ev27"],
    "q.auditoria": ["#"],
    "q.salida.se6": ["ev18", "ev19", "ev20"],
    "q.salida.se7": ["ev18", "ev20"],
    # Notificación (AD-13, RF-GD-06, RF-GD-15): desastre creado, severidad cambiada y acuses de SE-8 (E-37).
    "q.notificacion": ["ev03", "ev04", "ev29"],
    "q.salida.se8": ["ev28"],
}

# Usuario de cada servicio: qué cola lee y si publica en el exchange. Nadie puede configurar topología.
USUARIOS = {
    "reconstruccion": {"lee": ["q.reconstruccion"], "publica": True},
    "consulta-programa": {"lee": ["q.consulta-programa"], "publica": False},
    "evaluacion-danos": {"lee": ["q.evaluacion-danos"], "publica": True},
    "consulta-danos": {"lee": ["q.consulta-danos"], "publica": False},
    "gestion-desastres": {"lee": ["q.gestion-desastres"], "publica": True},
    "consulta": {"lee": ["q.consulta"], "publica": False},
    "auditoria": {"lee": ["q.auditoria"], "publica": False},
    "adaptador-se6": {"lee": ["q.salida.se6"], "publica": True},
    "adaptador-se7": {"lee": ["q.salida.se7"], "publica": True},
    "notificacion": {"lee": ["q.notificacion"], "publica": True},
    "adaptador-se8": {"lee": ["q.salida.se8"], "publica": True},
    "adaptador-se2": {"lee": [], "publica": True},
}

# Límites de las colas acotadas (AD-02). Al llenarse rechazan la publicación: el evento se queda en la bandeja.
MAX_MENSAJES = 100_000
MAX_BYTES = 256 * 1024 * 1024
LIMITE_ENTREGAS = 5


def hash_clave(usuario: str, clave: str) -> str:
    """Formato rabbit_password_hashing_sha256: base64(sal de 4 bytes + sha256(sal + clave))."""
    # Sal fija por usuario para que el archivo no cambie en cada ejecución (solo desarrollo).
    sal = hashlib.sha256(("plataforma:" + usuario).encode()).digest()[:4]
    return base64.b64encode(sal + hashlib.sha256(sal + clave.encode()).digest()).decode()


def clave_de(usuario: str) -> str:
    variable = "BUS_CLAVE_" + usuario.upper().replace("-", "_")
    return os.environ.get(variable, usuario.replace("-", "_") + "_local")


def regex_de(nombres):
    if not nombres:
        return "^$"
    return "^(" + "|".join(n.replace(".", r"\.") for n in nombres) + ")$"


def main():
    usuarios = [{
        "name": "admin",
        "password_hash": hash_clave("admin", clave_de("admin")),
        "hashing_algorithm": "rabbit_password_hashing_sha256",
        "tags": ["administrator"],
        "limits": {},
    }]
    permisos = [{"user": "admin", "vhost": "/", "configure": ".*", "write": ".*", "read": ".*"}]
    for usuario, acceso in USUARIOS.items():
        usuarios.append({
            "name": usuario,
            "password_hash": hash_clave(usuario, clave_de(usuario)),
            "hashing_algorithm": "rabbit_password_hashing_sha256",
            "tags": [],
            "limits": {},
        })
        permisos.append({
            "user": usuario,
            "vhost": "/",
            "configure": "^$",
            "write": regex_de([EXCHANGE]) if acceso["publica"] else "^$",
            "read": regex_de(acceso["lee"]),
        })

    colas, enlaces = [], []

    def cola(nombre):
        colas.append({
            "name": nombre, "vhost": "/", "durable": True, "auto_delete": False,
            # RabbitMQ exige el exchange de mensajes muertos en los argumentos cuando se da su clave de ruteo.
            "arguments": {"x-queue-type": "quorum", "x-dead-letter-exchange": DLX, "x-dead-letter-routing-key": nombre},
        })
        muerta = "dlq." + nombre[2:]
        colas.append({
            "name": muerta, "vhost": "/", "durable": True, "auto_delete": False,
            "arguments": {"x-queue-type": "quorum"},
        })
        enlaces.append({"source": DLX, "vhost": "/", "destination": muerta, "destination_type": "queue",
                        "routing_key": nombre, "arguments": {}})

    for nombre, eventos in COLAS.items():
        cola(nombre)
        for evento in eventos:
            clave = "#" if evento == "#" else evento + ".#"
            enlaces.append({"source": EXCHANGE, "vhost": "/", "destination": nombre, "destination_type": "queue",
                            "routing_key": clave, "arguments": {}})
    cola("q.sin-ruta")
    enlaces.append({"source": SIN_RUTA, "vhost": "/", "destination": "q.sin-ruta", "destination_type": "queue",
                    "routing_key": "", "arguments": {}})

    acotada = {"max-length": MAX_MENSAJES, "max-length-bytes": MAX_BYTES, "overflow": "reject-publish"}
    definiciones = {
        "rabbitmq_version": "4",
        "users": usuarios,
        "vhosts": [{"name": "/"}],
        "permissions": permisos,
        "exchanges": [
            {"name": EXCHANGE, "vhost": "/", "type": "topic", "durable": True, "auto_delete": False, "internal": False,
             "arguments": {"alternate-exchange": SIN_RUTA}},
            {"name": SIN_RUTA, "vhost": "/", "type": "fanout", "durable": True, "auto_delete": False, "internal": False,
             "arguments": {}},
            {"name": DLX, "vhost": "/", "type": "direct", "durable": True, "auto_delete": False, "internal": False,
             "arguments": {}},
        ],
        "queues": colas,
        "bindings": enlaces,
        "policies": [
            {"vhost": "/", "name": "plataforma-colas", "pattern": r"^q\.", "apply-to": "queues", "priority": 10,
             "definition": dict(acotada, **{"delivery-limit": LIMITE_ENTREGAS, "dead-letter-exchange": DLX,
                                             "dead-letter-strategy": "at-least-once"})},
            {"vhost": "/", "name": "plataforma-dlq", "pattern": r"^dlq\.", "apply-to": "queues", "priority": 10,
             "definition": acotada},
        ],
    }
    destino = Path(__file__).with_name("definiciones.json")
    destino.write_text(json.dumps(definiciones, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"{destino}: {len(colas)} colas, {len(enlaces)} enlaces, {len(usuarios)} usuarios.")


if __name__ == "__main__":
    main()
