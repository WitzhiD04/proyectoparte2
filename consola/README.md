# Consola web (Angular)

Tres consolas en una sola aplicación: autoridad, responsable y auditor. El perfil se elige arriba a la derecha.

```bash
npm install
npm start        # http://localhost:4200, envía /v1 a los servicios en localhost:5080 y 5081 (proxy.conf.json)
npm run build
```

Con Docker Compose no hace falta nada de esto: la consola queda en http://localhost:8080.
