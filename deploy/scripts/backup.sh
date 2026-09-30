#!/bin/sh
set -eu
name="treinos-$(date -u +%Y%m%dT%H%M%SZ)"
podman run --image-volume=ignore --rm --pod treinos-pod --secret treinos-db-backup-password,type=mount,target=/run/secrets/password -v treinos-backups:/backups -v treinos-dp-keys:/keys:ro -v treinos-tls:/certs:ro --entrypoint sh localhost/treinos-maintenance:local -ec '
 umask 077
 mkdir "/backups/$1"
 export MYSQL_PWD="$(cat /run/secrets/password)"
 mariadb-dump -h 127.0.0.1 -u treinos_backup --single-transaction --skip-lock-tables treinos > "/backups/$1/database.sql"
 tar -cf "/backups/$1/keys.tar" -C /keys .
 tar -cf "/backups/$1/tls.tar" -C /certs .
' sh "$name"
printf 'Backup criado: %s\n' "$name"
