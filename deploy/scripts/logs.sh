#!/bin/sh
set -eu
podman logs --tail 100 -f "${1:-treinos-api}"
