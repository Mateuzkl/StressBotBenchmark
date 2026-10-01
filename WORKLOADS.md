# Reproducible workload modes

`LOGIN_ONLY` logs in, reads the protocol and replies to server-requested pings.
It does **not** start the brain, walking, combat, chat, cosmetics, periodic
heartbeats or idle turns. Enable `loginOnlyKeepAliveEnabled` and/or
`loginOnlyIdleTurnEnabled` explicitly if a long idle test needs them. The
legacy `loginOnly: true` setting still selects this mode.

`REALISTIC` uses one serial decision loop. Default ticks are 500–1000 ms.
Each bot alternates idle (5–60 seconds), walking (3–30), combat (5–45) and
social (5–15) periods. Combat is followed by 2–15 seconds of rest. Activity
weights choose states, not exact simultaneous population percentages.
Healing remains possible during resting periods. Outfits are opt-in.
REALISTIC always uses the brain, even for legacy scripts with `aiEnabled: false`.

`TORTURE` intentionally allows continuous gameplay and target refreshes, with
175–225 ms default ticks. `aiEnabled: false` retains the legacy independent
loops in this mode only. It is not evidence of realistic player capacity.
The packet cap is still enforced; torture does not bypass spell cooldowns.

## Running

```powershell
dotnet run -c Release -- --script=realistic --bots=600 --seed=180252 --duration=5m --items-otb=C:/server/data/items/items.otb
dotnet run -c Release -- --script=login-only --bots=600 --duration=5m
dotnet run -c Release -- --script=torture --bots=1000 --seed=180252 --duration=5m --items-otb=C:/server/data/items/items.otb
dotnet run -c Release -- --self-test
```

Set the host, accounts and ports in the scripts first. The OTB is read locally,
not copied or published. Without it, item parsing/walkability remain explicitly
heuristic. Unknown opcodes stop that payload; malformed packets are counted,
never scanned for apparent opcodes. Do not accept a performance run whose
parser/unknown counters are nonzero.

`walkIntervalMs`, `attackScanIntervalMs`, `chatIntervalMs`, spell/heal cooldowns,
`aiTickIntervalMinMs`, `aiTickIntervalMaxMs`, `dashboardIntervalMs`,
`pingbackMinIntervalMs` and `maxPacketsPerSecondPerBot` control their respective
paths. The default cap is 18 packets/s/bot (maximum 20). Server-requested pongs
are not suppressed by the optional heartbeat interval.

Stable per-bot seeds and independent random streams make schedules and jitter
repeatable across processes. Network timing, server decisions and combat RNG
are still nondeterministic: repeat A/B tests rather than expecting bit-identical
gameplay. RSA/XTEA keys remain cryptographically random.

## Admission and telemetry

`queueSize` bounds pending replaceable movement/turn/target-refresh requests.
Full or stale requests (`maxSendLagMsToDrop`) are rejected before writing.
Cancellation, login, logout, pings, inventory actions and spell/chat text are
never age-dropped or reordered. No partial encrypted frame is discarded.
Read-loop ping replies are awaited and belong to the session lifetime.

Action totals count completed socket writes, not attempted decisions. Chats,
spells, heals, potions and outfits have separate counters. Headless telemetry
includes states, actions/s/bot, packet/byte rates, bot CPU (100% = one core),
RSS, drops, queue-full events, parser errors and unknown opcode breakdowns.
Queue/send p95/p99 use fixed histograms and report bucket upper bounds;
they are cumulative for the run, not rolling exact percentiles. No sample list
grows with packet count. Serial decisions avoid accumulating duplicate queued
actions; no packet-coalescing mechanism is claimed.

Headless logs also expose queue/send averages and maxima, packets/s/bot, and
separate stale drops from queue-full rejections. Age-dropped waits are included
in the queue histogram; silently excluding the slowest rejected requests would
make its tail misleading. TORTURE reports `ActiveContinuous`, not the unused
REALISTIC schedule's initial state distribution.

The default global login delay remains 650 ms. An explicitly smaller delay is
honored with a warning and applies to retries too. Use it only with compatible
limits on an isolated server. Never raise production IP or packet limits merely
to make a stress run pass.
