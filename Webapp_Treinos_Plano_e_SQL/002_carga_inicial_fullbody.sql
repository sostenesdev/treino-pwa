-- 002_carga_inicial_fullbody.sql
-- Importa o plano A/B/C para UMA conta existente, sem criar senha ou usuário.
-- Contrato: migrações 001 e 003 aplicadas + conta criada via ASP.NET Identity.
-- A conta é provisionada pelo administrador; não existe cadastro público.
-- Cliente: mariadb (MariaDB 11.8). DELIMITER é comando do cliente, não da API.
-- Na MESMA conexão, antes de SOURCE deste arquivo:
-- SET @seed_user_id = 'UUID retornado por GET /api/auth/me';
-- SOURCE /caminho/002_carga_inicial_fullbody.sql;
-- Não usar --force. Não colocar a senha do banco na linha de comando.
-- Idempotência: fullbody-3dias-v1 por usuário. Reexecução não sobrescreve edições
-- nem recria itens apagados. Não cria sessões concluídas ou cargas fictícias.
SET NAMES utf8mb4;
SET time_zone = '+00:00';

DELIMITER //
BEGIN NOT ATOMIC
    DECLARE v_user_id CHAR(36) DEFAULT NULL;
    DECLARE v_requested_user_id CHAR(36) DEFAULT NULL;
    DECLARE v_plan_id CHAR(36) DEFAULT NULL;
    DECLARE v_template_a CHAR(36) DEFAULT NULL;
    DECLARE v_template_b CHAR(36) DEFAULT NULL;
    DECLARE v_template_c CHAR(36) DEFAULT NULL;
    DECLARE v_imported INT DEFAULT 0;
    DECLARE v_revision BIGINT UNSIGNED DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    IF @seed_user_id IS NULL OR CHAR_LENGTH(TRIM(@seed_user_id)) <> 36 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Defina @seed_user_id com o UUID de uma conta existente.';
    END IF;
    SET v_requested_user_id = LOWER(TRIM(@seed_user_id));
    START TRANSACTION;

    -- Localizar a conta; a primeira trava de negócio será user_sync_state.
    SELECT id INTO v_user_id FROM app_users
      WHERE id = v_requested_user_id;
    IF v_user_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Conta não encontrada. Peça ao administrador para cadastrá-la antes do seed.';
    END IF;

    INSERT INTO user_sync_state (user_id) VALUES (v_user_id)
      ON DUPLICATE KEY UPDATE user_id = VALUES(user_id);
    SELECT current_revision INTO v_revision FROM user_sync_state
      WHERE user_id = v_user_id FOR UPDATE;

    SELECT COUNT(*) INTO v_imported FROM seed_imports
      WHERE user_id = v_user_id AND seed_key = 'fullbody-3dias-v1';
    IF v_imported = 0 THEN
        SET v_plan_id = UUID();
        SET v_template_a = UUID();
        SET v_template_b = UUID();
        SET v_template_c = UUID();

        INSERT INTO exercises
          (id, user_id, seed_code, name, muscle_group, equipment,
           measurement_type, load_kind, load_basis, load_unit, instructions)
        VALUES
          (UUID(), v_user_id, 'fb-v1-agachamento-livre', 'Agachamento livre', 'Pernas e glúteos', 'Barra', 'reps', 'external', 'total', 'kg', 'Carga total, incluindo a barra. Alternativa na ficha A: hack.'),
          (UUID(), v_user_id, 'fb-v1-hack', 'Agachamento no hack', 'Pernas e glúteos', 'Máquina hack', 'reps', 'external', 'machine_display', 'kg', 'Alternativa ao agachamento livre. Comparar a evolução nesta mesma máquina.'),
          (UUID(), v_user_id, 'fb-v1-supino-reto-halteres', 'Supino reto com halteres', 'Peito', 'Halteres e banco', 'reps', 'external', 'per_hand', 'kg', 'Registrar o peso de cada halter.'),
          (UUID(), v_user_id, 'fb-v1-remada-maquina', 'Remada na máquina com peito apoiado', 'Costas', 'Máquina com apoio de peito', 'reps', 'external', 'machine_display', 'kg', 'Registrar a carga indicada na máquina.'),
          (UUID(), v_user_id, 'fb-v1-mesa-flexora', 'Mesa flexora', 'Posteriores de coxa', 'Mesa flexora', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-elevacao-lateral', 'Elevação lateral', 'Ombros', 'Halteres', 'reps', 'external', 'per_hand', 'kg', 'Registrar o peso de cada halter.'),
          (UUID(), v_user_id, 'fb-v1-triceps-corda', 'Tríceps na polia com corda', 'Tríceps', 'Polia com corda', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-panturrilha-em-pe', 'Panturrilha em pé', 'Panturrilhas', 'Máquina', 'reps', 'external', 'machine_display', 'kg', 'Manter o mesmo equipamento para comparar as cargas.'),
          (UUID(), v_user_id, 'fb-v1-prancha', 'Prancha abdominal', 'Abdômen', 'Colchonete', 'duration', 'bodyweight', 'added_weight', 'kg', 'Registrar segundos. Carga adicional é opcional; não somar o peso corporal.'),
          (UUID(), v_user_id, 'fb-v1-terra-romeno', 'Levantamento terra romeno', 'Posteriores e glúteos', 'Barra', 'reps', 'external', 'total', 'kg', 'Quadril para trás, joelhos levemente flexionados. Descer apenas preservando a posição da coluna. Registrar carga total com a barra.'),
          (UUID(), v_user_id, 'fb-v1-supino-inclinado', 'Supino inclinado com halteres', 'Peito', 'Halteres e banco inclinado', 'reps', 'external', 'per_hand', 'kg', 'Registrar o peso de cada halter.'),
          (UUID(), v_user_id, 'fb-v1-puxada-frente', 'Puxada pela frente, pegada confortável', 'Costas', 'Polia alta', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-afundo-reverso', 'Afundo reverso com halteres', 'Pernas e glúteos', 'Halteres', 'reps', 'external', 'per_hand', 'kg', 'Repetições por perna. Descansar após ambas. Registrar o peso de cada halter.'),
          (UUID(), v_user_id, 'fb-v1-desenvolvimento-sentado', 'Desenvolvimento de ombros sentado', 'Ombros', 'Halteres e banco', 'reps', 'external', 'per_hand', 'kg', 'Nesta carga inicial, adotar halteres; registrar o peso de cada halter.'),
          (UUID(), v_user_id, 'fb-v1-rosca-direta', 'Rosca direta', 'Bíceps', 'Barra', 'reps', 'external', 'total', 'kg', 'Nesta carga inicial, adotar barra e registrar o peso total.'),
          (UUID(), v_user_id, 'fb-v1-panturrilha-sentada', 'Panturrilha sentada', 'Panturrilhas', 'Máquina', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-leg-press', 'Leg press', 'Pernas e glúteos', 'Leg press', 'reps', 'external', 'machine_display', 'kg', 'Usar uma convenção constante de carga e o mesmo equipamento.'),
          (UUID(), v_user_id, 'fb-v1-supino-maquina', 'Supino na máquina', 'Peito', 'Máquina', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-remada-baixa', 'Remada baixa na polia', 'Costas', 'Polia baixa', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-cadeira-flexora', 'Cadeira flexora', 'Posteriores de coxa', 'Cadeira flexora', 'reps', 'external', 'machine_display', 'kg', NULL),
          (UUID(), v_user_id, 'fb-v1-rosca-martelo', 'Rosca martelo', 'Bíceps e antebraços', 'Halteres', 'reps', 'external', 'per_hand', 'kg', 'Registrar o peso de cada halter.'),
          (UUID(), v_user_id, 'fb-v1-triceps-polia', 'Tríceps na polia', 'Tríceps', 'Polia', 'reps', 'external', 'machine_display', 'kg', 'Manter o mesmo acessório ao comparar registros. Item separado do tríceps com corda da ficha A.'),
          (UUID(), v_user_id, 'fb-v1-abdominal-polia', 'Abdominal na polia', 'Abdômen', 'Polia', 'reps', 'external', 'machine_display', 'kg', NULL);

        INSERT INTO workout_plans
          (id, user_id, seed_code, name, description, duration_weeks,
           introductory_sets, rir_min, rir_max, warmup_seconds_min, warmup_seconds_max,
           cardio_seconds_min, cardio_seconds_max, instructions)
        VALUES
          (v_plan_id, v_user_id, 'fullbody-3dias-v1', 'Full Body - 3 dias',
           'Plano A/B/C para hipertrofia e redução de gordura, em dias alternados.',
           8, 2, 1, 3, 300, 480, 900, 1200,
           'Dias sugeridos: segunda, quarta e sexta. Musculação: cerca de 60-80 min. Aquecer 5-8 min e fazer séries progressivas nos primeiros exercícios de pernas e membros superiores; aquecimento não conta nas séries. Na primeira semana, fazer 2 séries onde a ficha indica 3. Depois usar o volume completo se houver recuperação. Terminar a maioria das séries com 1-3 repetições em reserva. Ao alcançar o topo da faixa em todas as séries com boa técnica e reserva, aumentar pelo menor incremento disponível, em geral 2-5%. Após o treino, 15-20 min de caminhada rápida ou bicicleta em ritmo que permita conversar. Duas caminhadas de 30 min nos dias livres, progredindo conforme recuperação. Dor articular ou lombar: interromper e ajustar com o profissional da academia.');

        INSERT INTO workout_templates
          (id, user_id, plan_id, code, name, suggested_iso_weekday, position, notes)
        VALUES
          (v_template_a, v_user_id, v_plan_id, 'A', 'Full Body A', 1, 1,
           'Agachamento, supino e remada. Escolher agachamento livre OU hack.'),
          (v_template_b, v_user_id, v_plan_id, 'B', 'Full Body B', 3, 2,
           'Quadril, puxada e trabalho unilateral. Afundo: repetições por perna, descanso após ambas.'),
          (v_template_c, v_user_id, v_plan_id, 'C', 'Full Body C', 5, 3,
           'Máquinas e complementos para fechar a semana.');

        INSERT INTO workout_template_items
          (id, user_id, template_id, exercise_id, position, target_sets,
           reps_min, reps_max, duration_seconds_min, duration_seconds_max,
           repetition_scope, rest_seconds_min, rest_seconds_max, rest_scope, notes)
        VALUES
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-agachamento-livre'), 1, 3, 6, 10, NULL, NULL, 'total', 120, 180, 'after_set', 'Alternativa permitida: agachamento no hack.'),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-supino-reto-halteres'), 2, 3, 8, 12, NULL, NULL, 'total', 120, 180, 'after_set', NULL),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-remada-maquina'), 3, 3, 8, 12, NULL, NULL, 'total', 120, 120, 'after_set', NULL),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-mesa-flexora'), 4, 3, 10, 15, NULL, NULL, 'total', 90, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-elevacao-lateral'), 5, 2, 12, 20, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-triceps-corda'), 6, 2, 10, 15, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-panturrilha-em-pe'), 7, 3, 10, 15, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_a, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-prancha'), 8, 2, NULL, NULL, 30, 45, 'total', 60, 60, 'after_set', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-terra-romeno'), 1, 3, 8, 10, NULL, NULL, 'total', 120, 180, 'after_set', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-supino-inclinado'), 2, 3, 8, 12, NULL, NULL, 'total', 120, 180, 'after_set', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-puxada-frente'), 3, 3, 8, 12, NULL, NULL, 'total', 120, 120, 'after_set', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-afundo-reverso'), 4, 3, 8, 12, NULL, NULL, 'per_side', 120, 120, 'after_both_sides', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-desenvolvimento-sentado'), 5, 2, 8, 12, NULL, NULL, 'total', 120, 120, 'after_set', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-rosca-direta'), 6, 2, 10, 15, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_b, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-panturrilha-sentada'), 7, 3, 12, 20, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-leg-press'), 1, 3, 10, 15, NULL, NULL, 'total', 120, 180, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-supino-maquina'), 2, 3, 8, 12, NULL, NULL, 'total', 120, 120, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-remada-baixa'), 3, 3, 8, 12, NULL, NULL, 'total', 120, 120, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-cadeira-flexora'), 4, 3, 10, 15, NULL, NULL, 'total', 90, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-elevacao-lateral'), 5, 2, 12, 20, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-rosca-martelo'), 6, 2, 10, 15, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-triceps-polia'), 7, 2, 10, 15, NULL, NULL, 'total', 60, 90, 'after_set', NULL),
          (UUID(), v_user_id, v_template_c, (SELECT id FROM exercises WHERE user_id = v_user_id AND seed_code = 'fb-v1-abdominal-polia'), 8, 2, 10, 15, NULL, NULL, 'total', 60, 90, 'after_set', NULL);

        INSERT INTO workout_item_alternatives (user_id, template_item_id, exercise_id)
        SELECT v_user_id, ti.id, ex.id
        FROM workout_template_items ti
        JOIN exercises ex ON ex.user_id = v_user_id AND ex.seed_code = 'fb-v1-hack'
        WHERE ti.user_id = v_user_id AND ti.template_id = v_template_a AND ti.position = 1;

        -- Sanidade antes do commit: ficha completa e uma alternativa.
        IF (SELECT COUNT(*) FROM workout_templates WHERE user_id = v_user_id AND plan_id = v_plan_id) <> 3
           OR (SELECT COUNT(*) FROM workout_template_items WHERE user_id = v_user_id AND template_id IN (v_template_a, v_template_b, v_template_c)) <> 23
           OR (SELECT COUNT(*) FROM exercises WHERE user_id = v_user_id AND seed_code LIKE 'fb-v1-%') <> 23 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Contagem inesperada no seed; transação revertida.';
        END IF;

        INSERT INTO seed_imports (user_id, seed_key) VALUES (v_user_id, 'fullbody-3dias-v1');

        -- O seed altera várias definições: avisar clientes existentes para
        -- baixar um novo snapshot SEM descartar sessões/outbox locais pendentes.
        SET v_revision = v_revision + 1;
        UPDATE user_sync_state SET current_revision = v_revision,
          updated_at_utc = UTC_TIMESTAMP(6) WHERE user_id = v_user_id;
        INSERT INTO sync_changes
          (user_id, revision, entity_type, entity_id, change_kind, payload_json)
        VALUES
          (v_user_id, v_revision, 'bootstrap', NULL, 'reset_required',
           JSON_OBJECT('reason', 'fullbody_seed', 'seedKey', 'fullbody-3dias-v1'));
    END IF;
    COMMIT;
    SELECT CASE WHEN v_imported = 0 THEN 'importado' ELSE 'ja_importado_sem_alteracoes' END AS resultado,
           v_user_id AS user_id, 'fullbody-3dias-v1' AS seed_key;
END//
DELIMITER ;

-- Conferência opcional, na mesma conexão:
SELECT t.code, t.name, COUNT(i.id) AS exercicios_na_ficha
FROM workout_templates t
JOIN workout_plans p ON p.user_id = t.user_id AND p.id = t.plan_id
LEFT JOIN workout_template_items i ON i.user_id = t.user_id AND i.template_id = t.id
WHERE t.user_id = LOWER(TRIM(@seed_user_id)) AND p.seed_code = 'fullbody-3dias-v1'
GROUP BY t.code, t.name, t.position ORDER BY t.position;
-- Esperado na primeira importação: A=8, B=7, C=8.
