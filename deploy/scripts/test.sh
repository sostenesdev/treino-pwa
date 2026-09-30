#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
. ./deploy/images.conf
./deploy/scripts/build.sh
podman build --build-arg DOTNET_SDK_IMAGE="$DOTNET_SDK_IMAGE" -f deploy/Containerfile.tests -t localhost/treinos-tests:local .
podman build --build-arg PLAYWRIGHT_IMAGE="$PLAYWRIGHT_IMAGE" -f deploy/Containerfile.e2e -t localhost/treinos-e2e:local .
test_id="treinos-ci-$(date +%s)-$$"
test_password="$(od -An -N24 -tx1 /dev/urandom | tr -d ' \n')"
test_secret="$test_id-password"
test_pod="$test_id-pod"
test_keys="$test_id-keys"
test_data="$test_id-data"
test_tls="$test_id-tls"
cleanup() {
    podman pod rm -f "$test_pod" >/dev/null 2>&1 || true
    podman secret rm "$test_secret" >/dev/null 2>&1 || true
    podman volume rm "$test_keys" "$test_data" "$test_tls" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
printf '%s' "$test_password" | podman secret create "$test_secret" - >/dev/null
podman pod create --name "$test_pod" >/dev/null
podman run --image-volume=ignore -d --name "$test_id-db" --pod "$test_pod" -v "$test_data:/var/lib/mysql" -v "$PWD/deploy/db-init.sh:/docker-entrypoint-initdb.d/10-treinos-users.sh:ro" --secret "$test_secret",type=mount,target=/run/secrets/db-root-password --secret "$test_secret",type=mount,target=/run/secrets/db-migrator-password --secret "$test_secret",type=mount,target=/run/secrets/db-backup-password -e MARIADB_DATABASE=treinos -e MARIADB_USER=treinos_api -e MARIADB_PASSWORD="$test_password" -e MARIADB_ROOT_PASSWORD="$test_password" "$DB_IMAGE" >/dev/null
connection="Server=127.0.0.1;Database=treinos;User ID=treinos_api;Password=$test_password;"
migration_connection="Server=127.0.0.1;Database=treinos;User ID=treinos_migrator;Password=$test_password;"
podman run --image-volume=ignore --rm --pod "$test_pod" -e ConnectionStrings__Treinos="$migration_connection" -v "$PWD/database:/opt/treinos/database:ro" localhost/treinos-migrator:local
podman run --image-volume=ignore --rm --pod "$test_pod" --secret "$test_secret",type=mount,target=/run/secrets/password -e ConnectionStrings__Treinos="$connection" -v "$test_keys:/keys" -e DataProtection__KeysPath=/keys -e BOOTSTRAP_ADMIN_EMAIL=ci@example.invalid -e BOOTSTRAP_ADMIN_NAME=CI -e BOOTSTRAP_ADMIN_PASSWORD_FILE=/run/secrets/password localhost/treinos-api:local bootstrap-admin
podman run --image-volume=ignore -i --rm --pod "$test_pod" -e MYSQL_PWD="$test_password" -v "$PWD/database:/opt/treinos/database:ro" --entrypoint sh localhost/treinos-maintenance:local -ec 'id=$(mariadb -h 127.0.0.1 -u treinos_api -N treinos -e "SELECT id FROM app_users WHERE email= '\''ci@example.invalid'\''"); printf "SET @seed_user_id= '\''%s'\''; SOURCE /opt/treinos/database/seeds/002_carga_inicial_fullbody.sql;\n" "$id" | mariadb -h 127.0.0.1 -u treinos_api treinos'
podman run --image-volume=ignore -i --rm --pod "$test_pod" -e MYSQL_PWD="$test_password" -v "$PWD/database:/opt/treinos/database:ro" --entrypoint sh localhost/treinos-maintenance:local -ec 'id=$(mariadb -h 127.0.0.1 -u treinos_api -N treinos -e "SELECT id FROM app_users WHERE email= '\''ci@example.invalid'\''"); printf "SET @seed_user_id= '\''%s'\''; SOURCE /opt/treinos/database/seeds/002_carga_inicial_fullbody.sql;\n" "$id" | mariadb -h 127.0.0.1 -u treinos_api treinos'
podman run --image-volume=ignore --rm --pod "$test_pod" -e MYSQL_PWD="$test_password" --entrypoint sh localhost/treinos-maintenance:local -ec '
 test "$(mariadb -h 127.0.0.1 -u treinos_api -N treinos -e "SELECT COUNT(*) FROM exercises")" = 23
 if mariadb -h 127.0.0.1 -u treinos_api treinos -e "CREATE TABLE forbidden_ddl(id INT)" 2>/dev/null; then
  echo "API recebeu permissão de DDL indevida" >&2
  exit 1
 fi
'

podman run --image-volume=ignore --rm --pod "$test_pod" -e TREINOS_TEST_CONNECTION="$connection" localhost/treinos-tests:local
podman run --image-volume=ignore --rm --pod "$test_pod" --entrypoint npm localhost/treinos-e2e:local test
podman run --image-volume=ignore -d --name "$test_id-mailpit" --pod "$test_pod" "$MAILPIT_IMAGE" >/dev/null
podman run --image-volume=ignore -d --name "$test_id-api" --pod "$test_pod" -e ConnectionStrings__Treinos="$connection" -v "$test_keys:/keys" -e DataProtection__KeysPath=/keys -e ASPNETCORE_ENVIRONMENT=Development -e Email__Enabled=true -e Email__Host=127.0.0.1 -e Email__Port=1025 -e Email__TlsMode=None -e Email__FromAddress=treinos@example.invalid -e App__PublicBaseUrl=https://localhost:8443 localhost/treinos-api:local >/dev/null
podman run --image-volume=ignore -d --name "$test_id-web" --pod "$test_pod" -v "$test_tls:/data" -v "$PWD/deploy/Caddyfile.test:/etc/caddy/Caddyfile:ro" localhost/treinos-web:local >/dev/null
podman run --image-volume=ignore --rm --pod "$test_pod" -e TREINOS_TEST_EMAIL=ci@example.invalid -e TREINOS_TEST_PASSWORD="$test_password" localhost/treinos-e2e:local --grep-invert 'admin gerencia'
# Each browser suite retains the production limit of ten auth requests/minute.
# Restart between suites to give the independent architecture journey a fresh window.
podman restart "$test_id-api" >/dev/null
podman run --image-volume=ignore --rm --pod "$test_pod" -e TREINOS_TEST_EMAIL=ci@example.invalid -e TREINOS_TEST_PASSWORD="$test_password" localhost/treinos-e2e:local tests/e2e/architecture.spec.ts
podman run --image-volume=ignore --rm --pod "$test_pod" -e MYSQL_PWD="$test_password" -v "$test_keys:/keys:ro" -v "$test_tls:/certs:ro" --entrypoint sh localhost/treinos-maintenance:local -ec '
 mariadb-dump -h 127.0.0.1 -u treinos_backup --single-transaction --skip-lock-tables treinos > /tmp/treinos.sql
 mariadb -h 127.0.0.1 -u root -e "CREATE DATABASE treinos_restore CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"
 mariadb -h 127.0.0.1 -u root treinos_restore < /tmp/treinos.sql
 for table in app_users workout_sessions session_exercises session_sets user_sync_state sync_changes sync_operation_receipts email_outbox; do
  original=$(mariadb -h 127.0.0.1 -u root -N treinos -e "SELECT COUNT(*) FROM $table")
  restored=$(mariadb -h 127.0.0.1 -u root -N treinos_restore -e "SELECT COUNT(*) FROM $table")
  test "$original" = "$restored"
 done
 mkdir /tmp/restoredkeys /tmp/restoredtls
 tar -cf /tmp/keys.tar -C /keys .
 tar -cf /tmp/tls.tar -C /certs .
 tar -xf /tmp/keys.tar -C /tmp/restoredkeys
 tar -xf /tmp/tls.tar -C /tmp/restoredtls
 diff -r /keys /tmp/restoredkeys
 diff -r /certs /tmp/restoredtls
 printf "Backup/restauração: banco, chaves e certificados conferidos.\n"
'
