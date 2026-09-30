#!/bin/sh
set -eu
: "${BOOTSTRAP_ADMIN_EMAIL:?Defina BOOTSTRAP_ADMIN_EMAIL}"
: "${BOOTSTRAP_ADMIN_NAME:?Defina BOOTSTRAP_ADMIN_NAME}"
podman run --image-volume=ignore --rm --pod treinos-pod --secret treinos-db-password,type=mount,target=/run/secrets/db-password --secret treinos-admin-password,type=mount,target=/run/secrets/admin-password -e 'ConnectionStrings__Treinos=Server=127.0.0.1;Port=3306;Database=treinos;User ID=treinos_api;' -e Database__PasswordFile=/run/secrets/db-password -e BOOTSTRAP_ADMIN_PASSWORD_FILE=/run/secrets/admin-password -e BOOTSTRAP_ADMIN_EMAIL -e BOOTSTRAP_ADMIN_NAME -e BOOTSTRAP_PROMOTE_USER_ID -v treinos-dp-keys:/var/lib/treinos/keys localhost/treinos-api:local bootstrap-admin
