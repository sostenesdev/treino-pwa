#!/bin/sh
set -eu
systemctl --user stop treinos-web.service treinos-api.service treinos-migrator.service treinos-db.service treinos-pod.service
