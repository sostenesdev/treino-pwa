#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
. ./deploy/images.conf
podman build --build-arg DOTNET_SDK_IMAGE="$DOTNET_SDK_IMAGE" --build-arg DOTNET_ASPNET_IMAGE="$DOTNET_ASPNET_IMAGE" -f deploy/Containerfile.api -t localhost/treinos-api:local .
podman build --build-arg DOTNET_SDK_IMAGE="$DOTNET_SDK_IMAGE" --build-arg DOTNET_ASPNET_IMAGE="$DOTNET_ASPNET_IMAGE" -f deploy/Containerfile.migrator -t localhost/treinos-migrator:local .
podman build --build-arg NODE_IMAGE="$NODE_IMAGE" --build-arg CADDY_IMAGE="$CADDY_IMAGE" -f deploy/Containerfile.web -t localhost/treinos-web:local .
podman build --build-arg DB_IMAGE="$DB_IMAGE" -f deploy/Containerfile.maintenance -t localhost/treinos-maintenance:local .
