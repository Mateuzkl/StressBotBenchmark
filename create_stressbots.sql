-- StressBot: Criar contas e personagens para stress test
-- Rodar no banco de dados do TFS antes de iniciar o bot
-- Password: test123 (armazenada como SHA1, o TFS auto-migra para SHA256)

DELIMITER $$
DROP PROCEDURE IF EXISTS create_stressbots$$
CREATE PROCEDURE create_stressbots(IN bot_count INT, IN prefix_name VARCHAR(32))
BEGIN
    DECLARE i INT DEFAULT 1;
    DECLARE acc_name VARCHAR(32);
    DECLARE pwd_hash VARCHAR(105);
    DECLARE target_acc_id INT;
    DECLARE target_voc INT;
    DECLARE target_looktype INT;

    SET pwd_hash = SHA1('test123');
    IF prefix_name IS NULL OR prefix_name = '' THEN
        SET prefix_name = 'stressbot';
    END IF;

    WHILE i <= bot_count DO
        SET acc_name = CONCAT(prefix_name, '_', LPAD(i, 4, '0'));

        -- Garantir conta
        INSERT IGNORE INTO `accounts` (`name`, `password`, `type`, `creation`)
        VALUES (acc_name, pwd_hash, 1, UNIX_TIMESTAMP());

        -- Buscar account_id de forma segura e determinística
        SELECT `id` INTO target_acc_id FROM `accounts` WHERE `name` = acc_name LIMIT 1;

        -- Distribuir vocações equilibradas (1=Sorcerer, 2=Druid, 3=Paladin, 4=Knight)
        SET target_voc = (i % 4) + 1;
        SET target_looktype = CASE target_voc
            WHEN 1 THEN 130 -- Mage
            WHEN 2 THEN 144 -- Druid
            WHEN 3 THEN 129 -- Hunter / Paladin
            WHEN 4 THEN 131 -- Knight
            ELSE 128
        END;

        -- Inserir player associado explicitamente ao target_acc_id
        INSERT IGNORE INTO `players` (`name`, `account_id`, `group_id`, `level`, `vocation`,
            `health`, `healthmax`, `experience`, `mana`, `manamax`, `cap`, `soul`,
            `town_id`, `looktype`, `lookbody`, `lookfeet`, `lookhead`, `looklegs`,
            `posx`, `posy`, `posz`, `conditions`, `stamina`, `save`)
        VALUES (acc_name, target_acc_id, 1, 100, target_voc,
            1000, 1000, 15694800, 1000, 1000, 1500, 100,
            1, target_looktype, 68, 76, 78, 39,
            0, 0, 0, NULL, 2520, 1);

        SET i = i + 1;
    END WHILE;
END$$
DELIMITER ;

CALL create_stressbots(1000, 'teste123');
DROP PROCEDURE IF EXISTS create_stressbots;

SELECT COUNT(*) AS total_accounts FROM `accounts` WHERE `name` LIKE 'teste123_%';
SELECT COUNT(*) AS total_players FROM `players` WHERE `name` LIKE 'teste123_%';
