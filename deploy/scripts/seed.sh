#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${TREINOS_USER_ID:?Defina TREINOS_USER_ID com o UUID da conta}"
case "$TREINOS_USER_ID" in *[!0-9a-fA-F-]*|'') echo 'UUID inválido' >&2; exit 1;; esac
printf "SET @seed_user_id = '%s';\nSOURCE /opt/treinos/database/seeds/002_carga_inicial_fullbody.sql;\n" "$TREINOS_USER_ID" | podman run --image-volume=ignore -i --rm --pod treinos-pod --secret treinos-db-password,type=mount,target=/run/secrets/password -v "$PWD/database:/opt/treinos/database:ro" --entrypoint sh localhost/treinos-maintenance:local -ec 'export MYSQL_PWD="$(cat /run/secrets/password)"; exec mariadb --default-character-set=utf8mb4 -h 127.0.0.1 -u treinos_api treinos'
