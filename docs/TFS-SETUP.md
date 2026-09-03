# Configuração da branch tfs

Base: `StressBotBenchmark` em `8c86fab`, usando o código do repositório local
`../forgottenserver-downgrade-1.8-8.60` como referência.

A branch inclui manutenção de conexão, controle global de tentativas e presets
para o TFS. A adaptação completa do benchmark descrita no anexo ainda está pendente.

## Presets

`scripts/tfs-1000.json` usa 1000 bots, contas `test_001` até `test_1000`,
senha de teste `test123`, somente login e reconexão ativada. A largura de três
dígitos é mínima: o índice 1000 continua sendo `test_1000`. O bot envia `0x1E`
a cada cinco segundos e vira no próprio tile aproximadamente a cada minuto,
para atender ao ping e ao temporizador separado de inatividade do servidor.
As tentativas de conexão e reconexão compartilham o intervalo mínimo de 650 ms.
Sem falhas, a subida demora aproximadamente 11 minutos.

O arquivo `scripts/tfs.json` é copiado para a pasta de saída no build e publish.
Ele usa `127.0.0.1:7172`, 100 bots, contas `stressbot_0001` em diante,
senha de teste `test123`, somente login e reconexão desativada. O intervalo de
650 ms entre conexões fica acima dos 500 ms configurados no limitador do TFS.
Revise host, credenciais e quantidade nesse arquivo antes de compilar.

```powershell
dotnet restore
dotnet build -c Release
```

Após provisionar as contas correspondentes e compilar, execute:

```powershell
dotnet run -c Release --no-build -- --script=tfs-1000
```

`--bots=10` sobrescreve a quantidade, `--login-only` força somente manutenção
da conexão, `--duration=90s` ou `--duration=15m` limita o tempo total da execução
(incluindo a subida), e `--check-config` mostra a configuração sem conectar.
Sem duração, a execução continua até Ctrl+C. `--profile` e `--ramp` permanecem
pendentes.

## Configuração local do TFS

No `config.lua` do servidor, os valores para este ambiente de benchmark são:

```lua
maxPlayers = 1000
maxConnections = 2000
maxConnectionsPerIP = 1000
maxPacketsPerSecond = 25
connectionRateLimitAllowed = 10
connectionRateLimitMS = 500
performanceMetricsEnabled = true
slowTaskWarning = true
```

`maxPlayers` estava em 500, portanto somente aumentar o limite por IP não
permitiria 1000 jogadores. O nome da configuração é `maxConnectionsPerIP`
(IP em maiúsculas), já implementado em `src/configmanager.cpp` e aplicado em
`ConnectionManager::trackIPConnection`, em `src/connection.cpp`.

O `config.lua` é local e ignorado pelo Git. O `config.lua.dist` permanece com
os padrões de produção: `maxConnectionsPerIP = 10` e diagnósticos desativados.
Para desfazer os ajustes locais desta preparação, restaure `maxPlayers = 500`,
`maxConnectionsPerIP = 10`, `performanceMetricsEnabled = false` e
`slowTaskWarning = false`. Carregue a configuração na próxima inicialização
do servidor; esta preparação não reinicia processos.

O limite do packet backlog permanece em 128. As proteções de frequência de
conexões, tentativas de login e packets/s continuam com seus valores atuais.

Para compilar o TFS com os diagnósticos adicionais, acrescente
`-DENABLE_PACKET_BACKLOG_DIAGNOSTICS=ON` ao comando CMake de configuração já
usado para esse servidor. Essa opção foi confirmada no `CMakeLists.txt` e
ativa `PROTOCOLGAME_BACKLOG_DIAGNOSTICS`. Em execução, as métricas agregadas
incluem reactor queue, backlog, latências e tarefas lentas.

## Correções e limitações

- O layout de challenge `0x1F`, timestamp, byte aleatório, versão 860 e campos
  RSA do bot corresponde ao fluxo básico lido em `ProtocolGame`. Ainda é
  necessário validar a chave RSA e o fluxo completo com o servidor.
- O bot já abre o socket diretamente. A referência a API HTTP no README antigo
  está desatualizada em relação ao código desta versão.
- O ping agora usa `0x1E`, com envio periódico independente do parser de mapa.
- A primeira resposta é processada e `InWorld` exige o reconhecimento `0x0A`.
  Os pacotes recebidos têm checksum Adler32 e tamanho XTEA validados.
- O parser lê sequências de opcodes conhecidos e para ao encontrar um layout
  desconhecido. O combate ainda usa heurística de IDs; HP, mana e visibilidade
  não possuem parsing completo.
- A contagem TCP e a espera de envio são medidas; actions/s é calculado pelos
  contadores de ações. O painel inclui pings, falhas TCP e o último erro.
- O dashboard lê um array de bots estável. As tarefas são aguardadas antes de
  descartar recursos ou abrir a próxima conexão; Ctrl+C cancela a execução.
- O SQL usa `INSERT IGNORE` com `LAST_INSERT_ID()`, podendo associar personagens
  à conta errada. `LPAD(i, 3, '0')` também não comporta o índice 1000 corretamente.
  O preset usa quatro dígitos; o SQL precisa ser corrigido antes de provisionar
  suas contas. O TFS consultado armazena SHA-1 hexadecimal; o comentário sobre
  migração automática para SHA-256 no SQL atual está incorreto.
- Todos os envios de cada bot são espaçados em pelo menos 55 ms, abaixo de
  25 packets/s. Isso é um teto de envio, não um perfil de caça realista.
- Healing, perfis realistas, rampa, spawn pools,
  relatórios e confirmação acima de 500 bots ainda precisam ser implementados.

Nenhuma alteração no banco é executada pelo bot. Um build bem-sucedido não
valida a capacidade de manter 1000 bots; isso depende do teste no servidor.
