#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${TREINOS_USER_ID:?Defina TREINOS_USER_ID com o UUID da conta}"
: "${TREINOS_DB_PASSWORD:?Defina TREINOS_DB_PASSWORD}"
case "$TREINOS_USER_ID" in *[!0-9a-fA-F-]*|'') echo 'UUID inválido' >&2; exit 1;; esac
printf "SET @seed_user_id = '%s';\nSOURCE /opt/treinos/database/seeds/002_carga_inicial_fullbody.sql;\n" "$TREINOS_USER_ID" | podman run -i --rm --pod treinos-pod -e MYSQL_PWD="$TREINOS_DB_PASSWORD" -v "$PWD/database:/opt/treinos/database:ro" localhost/treinos-maintenance:local --default-character-set=utf8mb4 -h 127.0.0.1 -u treinos_api treinos
