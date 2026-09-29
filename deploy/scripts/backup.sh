#!/bin/sh
set -eu
: "${TREINOS_DB_PASSWORD:?Defina TREINOS_DB_PASSWORD}"
name="treinos-$(date -u +%Y%m%dT%H%M%SZ).sql"
podman run --rm --pod treinos-pod -e MYSQL_PWD="$TREINOS_DB_PASSWORD" -v treinos-backups:/backups docker.io/library/mariadb:11.8 sh -c 'mariadb-dump -h 127.0.0.1 -u treinos_api --single-transaction --routines treinos > "/backups/$1"' sh "$name"
printf 'Backup criado: %s\n' "$name"
