#!/bin/sh
set -eu
export MYSQL_PWD="$(cat /run/secrets/db-root-password)"
migration_password=$(sed "s/'/''/g" /run/secrets/db-migrator-password)
backup_password=$(sed "s/'/''/g" /run/secrets/db-backup-password)
mariadb -u root <<SQL
SET SESSION sql_mode='NO_BACKSLASH_ESCAPES';
REVOKE ALL PRIVILEGES ON treinos.* FROM 'treinos_api'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON treinos.* TO 'treinos_api'@'%';
CREATE USER 'treinos_migrator'@'%' IDENTIFIED BY '$migration_password';
GRANT ALL PRIVILEGES ON treinos.* TO 'treinos_migrator'@'%';
CREATE USER 'treinos_backup'@'%' IDENTIFIED BY '$backup_password';
GRANT SELECT, SHOW VIEW, TRIGGER ON treinos.* TO 'treinos_backup'@'%';
SQL
