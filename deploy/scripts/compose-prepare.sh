#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
umask 077
secrets_dir="${TREINOS_SECRETS_DIR:-./.secrets}"
mkdir -p "$secrets_dir"
for secret_name in db-root-password db-password db-migrator-password db-backup-password admin-password; do
    secret_path="$secrets_dir/$secret_name"
    if [ ! -s "$secret_path" ]; then
        od -An -N32 -tx1 /dev/urandom | tr -d ' \n' > "$secret_path"
    fi
done
printf 'Segredos preparados em %s. Arquivos existentes foram preservados.\n' "$secrets_dir"
