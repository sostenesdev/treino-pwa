-- 003_pwa_usuarios_email.sql
-- Migração de estrutura, aplicada UMA VEZ depois de 001_schema_treinos.sql.
-- Executar pelo migrator no pod. O arquivo 002 é seed de dados, não migração.
-- Preserva o schema 001 e os registros existentes. Não promove ninguém a admin.
-- DDL MariaDB pode fazer commits implícitos; usar journal/checksum de migrações.
-- Requer MariaDB 11.8. SQL revisado estaticamente, ainda não executado no servidor.
SET NAMES utf8mb4;
SET time_zone = '+00:00';

ALTER TABLE app_users
    ADD COLUMN app_role VARCHAR(16) NOT NULL DEFAULT 'common',
    ADD COLUMN must_change_password BOOLEAN NOT NULL DEFAULT FALSE,
    ADD CONSTRAINT ck_users_app_role CHECK (app_role IN ('administrator', 'common'));

-- A revisão é serializada POR USUÁRIO. Todas as escritas sincronizáveis,
-- inclusive REST online e seed, usam esta linha como primeira trava de negócio.
CREATE TABLE user_sync_state (
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    current_revision BIGINT UNSIGNED NOT NULL DEFAULT 0,
    min_available_revision BIGINT UNSIGNED NOT NULL DEFAULT 1,
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (user_id),
    CONSTRAINT fk_sync_state_user FOREIGN KEY (user_id)
        REFERENCES app_users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_sync_state_revision CHECK (
        min_available_revision >= 1 AND
        min_available_revision <= current_revision + 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO user_sync_state (user_id) SELECT id FROM app_users;

CREATE TABLE sync_changes (
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    revision BIGINT UNSIGNED NOT NULL,
    entity_type VARCHAR(20) NOT NULL,
    entity_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    change_kind VARCHAR(20) NOT NULL,
    entity_version BIGINT UNSIGNED NULL,
    payload_json JSON NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (user_id, revision),
    KEY ix_changes_retention (created_at_utc),
    CONSTRAINT fk_changes_user FOREIGN KEY (user_id)
        REFERENCES app_users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_change_revision CHECK (revision > 0),
    CONSTRAINT ck_change_type CHECK (
        entity_type IN ('exercise', 'plan', 'template', 'session', 'bootstrap')),
    CONSTRAINT ck_change_kind CHECK (change_kind IN ('upsert', 'delete', 'reset_required')),
    CONSTRAINT ck_change_shape CHECK (
        (change_kind = 'reset_required' AND entity_type = 'bootstrap' AND entity_id IS NULL)
        OR
        (change_kind IN ('upsert', 'delete') AND entity_type <> 'bootstrap' AND entity_id IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Guardar os recibos aplicados no MVP, sem TTL curto. Um retry antigo precisa
-- receber o mesmo resultado, inclusive quando o registro já foi excluído.
CREATE TABLE sync_operation_receipts (
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    operation_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    entity_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    operation_kind VARCHAR(12) NOT NULL,
    request_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    response_json JSON NOT NULL,
    applied_revision BIGINT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (user_id, operation_id),
    KEY ix_sync_receipt_entity (user_id, entity_id),
    CONSTRAINT fk_sync_receipt_user FOREIGN KEY (user_id)
        REFERENCES app_users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_sync_receipt_kind CHECK (operation_kind IN ('create', 'replace', 'delete')),
    CONSTRAINT ck_sync_receipt_revision CHECK (applied_revision > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE email_outbox (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    recipient_email VARCHAR(256) NOT NULL,
    template_key VARCHAR(40) NOT NULL,
    payload_ciphertext LONGBLOB NOT NULL,
    status VARCHAR(12) NOT NULL DEFAULT 'pending',
    attempts SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    available_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    token_expires_at_utc DATETIME(6) NOT NULL,
    lease_until_utc DATETIME(6) NULL,
    lease_token CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    last_error_code VARCHAR(80) NULL,
    provider_message_id VARCHAR(255) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    sent_at_utc DATETIME(6) NULL,
    PRIMARY KEY (id),
    KEY ix_email_outbox_dispatch (status, available_at_utc),
    KEY ix_email_outbox_owner (user_id, created_at_utc),
    CONSTRAINT fk_email_outbox_user FOREIGN KEY (user_id)
        REFERENCES app_users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_email_outbox_status CHECK (
        status IN ('pending', 'processing', 'sent', 'failed', 'expired')),
    CONSTRAINT ck_email_outbox_template CHECK (
        template_key IN ('password_reset', 'initial_access')),
    CONSTRAINT ck_email_outbox_expiry CHECK (token_expires_at_utc > created_at_utc),
    CONSTRAINT ck_email_outbox_lease CHECK (
        (lease_until_utc IS NULL AND lease_token IS NULL) OR
        (lease_until_utc IS NOT NULL AND lease_token IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- BootstrapAdmin deve usar UserManager/Identity no container de manutenção.
-- Contas existentes continuam common. Não definir senha, hash ou admin por SQL.
-- Novos usuários devem receber user_sync_state na transação de criação.
-- Não enfileirar e-mails enquanto Email.Enabled=false.
