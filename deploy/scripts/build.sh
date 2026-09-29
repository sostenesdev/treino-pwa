#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
podman build -f deploy/Containerfile.api -t localhost/treinos-api:local .
podman build -f deploy/Containerfile.migrator -t localhost/treinos-migrator:local .
podman build -f deploy/Containerfile.web -t localhost/treinos-web:local .
podman build -f deploy/Containerfile.maintenance -t localhost/treinos-maintenance:local .
