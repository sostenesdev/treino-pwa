#!/bin/sh
set -eu
: "${TREINOS_RESTORE_NAME:?Informe o diretório do backup no volume treinos-backups}"
: "${TREINOS_CONFIRM_RESTORE:?Defina TREINOS_CONFIRM_RESTORE=RESTORE para substituir os dados}"
[ "$TREINOS_CONFIRM_RESTORE" = RESTORE ] || exit 1
case "$TREINOS_RESTORE_NAME" in *[!a-zA-Z0-9-]*|'') echo 'Nome inválido' >&2; exit 1;; esac
systemctl --user stop treinos-web.service treinos-api.service
podman run --image-volume=ignore --rm --pod treinos-pod --secret treinos-db-migrator-password,type=mount,target=/run/secrets/password -v treinos-backups:/backups:ro -v treinos-dp-keys:/keys -v treinos-tls:/certs --entrypoint sh localhost/treinos-maintenance:local -ec '
 export MYSQL_PWD="$(cat /run/secrets/password)"
 test -s "/backups/$1/database.sql"
 tar -tf "/backups/$1/keys.tar" >/dev/null
 tar -tf "/backups/$1/tls.tar" >/dev/null
 mariadb -h 127.0.0.1 -u treinos_migrator treinos < "/backups/$1/database.sql"
 tar -xf "/backups/$1/keys.tar" -C /keys
 tar -xf "/backups/$1/tls.tar" -C /certs
' sh "$TREINOS_RESTORE_NAME"
systemctl --user restart treinos-migrator.service
systemctl --user start treinos-api.service treinos-web.service
