#!/bin/sh
# Permite que la réplica de PostgreSQL (segunda ubicación) se conecte para copiar los datos de forma continua.
if [ -n "$PGDATA" ] && [ -f "$PGDATA/pg_hba.conf" ]; then
    echo "host replication replicador all scram-sha-256" >> "$PGDATA/pg_hba.conf"
fi
