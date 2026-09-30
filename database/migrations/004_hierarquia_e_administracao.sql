-- Mantém a hierarquia existente e os dados importados: usuário -> treino -> ficha -> itens.
-- Exclusão de usuário é lógica para preservar registros, snapshots e recibos.
ALTER TABLE app_users ADD COLUMN deleted_at_utc DATETIME(6) NULL;
ALTER TABLE exercises MODIFY COLUMN equipment TEXT NULL, MODIFY COLUMN instructions LONGTEXT NULL;
ALTER TABLE session_exercises MODIFY COLUMN equipment_snapshot TEXT NULL, MODIFY COLUMN instructions_snapshot LONGTEXT NULL;
