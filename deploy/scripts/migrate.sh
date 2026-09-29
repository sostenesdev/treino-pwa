#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
podman run --rm --pod treinos-pod --secret treinos-db-password,type=mount,target=/run/secrets/db-password -e 'ConnectionStrings__Treinos=Server=127.0.0.1;Port=3306;Database=treinos;User ID=treinos_api;' -e Database__PasswordFile=/run/secrets/db-password -v "$PWD/database:/opt/treinos/database:ro" localhost/treinos-migrator:local
