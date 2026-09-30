#!/bin/sh
set -eu
cd "$(dirname "$0")"
./backup.sh
./build.sh
systemctl --user stop treinos-web.service treinos-api.service
systemctl --user restart treinos-migrator.service
systemctl --user start treinos-api.service treinos-web.service
