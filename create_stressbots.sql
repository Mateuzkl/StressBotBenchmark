-- StressBot: Criar contas e personagens para stress test
-- Rodar no banco de dados do TFS antes de iniciar o bot
-- Password: test123 (armazenada como SHA1, o TFS auto-migra para SHA256)

DELIMITER $$
DROP PROCEDURE IF EXISTS create_stressbots$$
CREATE PROCEDURE create_stressbots(IN bot_count INT)
BEGIN
    DECLARE i INT DEFAULT 1;
    DECLARE acc_name VARCHAR(32);
    DECLARE pwd_hash VARCHAR(105);

    SET pwd_hash = SHA1('test123');

    WHILE i <= bot_count DO
        SET acc_name = CONCAT('stressbot_', LPAD(i, 3, '0'));

        INSERT IGNORE INTO `accounts` (`name`, `password`, `type`, `creation`)
        VALUES (acc_name, pwd_hash, 1, UNIX_TIMESTAMP());

        INSERT IGNORE INTO `players` (`name`, `account_id`, `group_id`, `level`, `vocation`,
            `health`, `healthmax`, `experience`, `mana`, `manamax`, `cap`, `soul`,
            `town_id`, `looktype`, `lookbody`, `lookfeet`, `lookhead`, `looklegs`,
            `posx`, `posy`, `posz`, `conditions`, `stamina`, `save`)
        VALUES (acc_name, LAST_INSERT_ID(), 1, 8, 1,
            185, 185, 4200, 35, 35, 470, 100,
            1, 136, 68, 76, 78, 39,
            0, 0, 0, NULL, 2520, 1);

        SET i = i + 1;
    END WHILE;
END$$
DELIMITER ;

CALL create_stressbots(1000);
DROP PROCEDURE IF EXISTS create_stressbots;

SELECT COUNT(*) AS total_accounts FROM `accounts` WHERE `name` LIKE 'stressbot_%';
SELECT COUNT(*) AS total_players FROM `players` WHERE `name` LIKE 'stressbot_%';
