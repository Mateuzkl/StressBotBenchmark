using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using StressBotBenchmark.AI;
using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;
using StressBotBenchmark.World;

namespace StressBotBenchmark
{
    public class TibiaBot : IDisposable
    {
        private readonly string _name;
        private readonly string _password;
        private readonly BotConfig _config;
        private readonly BotMetrics _metrics;
        private readonly ConnectionPacer _connectionPacer;
        private readonly uint[] _xteaKey = new uint[4];
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private TcpClient? _client;
        private NetworkStream? _stream;
        private volatile bool _inWorld;
        private volatile bool _connected;
        private volatile bool _permanentFailure;
        private volatile string? _lastError;
        private int _started;
        private int _disposed;
        private long _lastSend;
        private long _lastPing;
        private bool _fightModesSent;

        // ── New: structured world state and protocol parser ──
        private readonly WorldState _worldState = new();
        private Protocol860Parser? _parser;
        private BotBrain? _brain;

        // Legacy compat — kept for dashboard until Phase 7 upgrades metrics
        private readonly List<uint> _recentMonsters = new();
        private readonly HashSet<uint> _allSeenMonsters = new();
        private readonly object _monsterLock = new();

        public TibiaBot(string name, string password, BotConfig config, BotMetrics metrics,
                        ConnectionPacer connectionPacer)
        {
            _name = name;
            _password = password;
            _config = config;
            _metrics = metrics;
            _connectionPacer = connectionPacer;
        }

        public string Name => _name;
        public bool InWorld => _inWorld;
        public bool Connected => _connected;
        public string? LastError => _lastError;
        public bool PermanentFailure => _permanentFailure;
        public PlayerStats? Stats { get; private set; }
        public string? LastServerMessage { get; private set; }
        public DateTime LastWalkTime { get; private set; } = DateTime.MinValue;
        public DateTime LastSpellTime { get; private set; } = DateTime.MinValue;
        public DateTime LastAttackTime { get; private set; } = DateTime.MinValue;
        public DateTime LastDamageTakenTime { get; private set; } = DateTime.MinValue;
        public int TrackedMonstersTotal => _worldState.CountVisibleMonsters();

        /// <summary>Structured world state for AI and dashboard access.</summary>
        public WorldState World => _worldState;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.Exchange(ref _started, 1) != 0)
                throw new InvalidOperationException("This bot has already been started.");
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            var token = lifetime.Token;
            int failures = 0;
            bool retry = false;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int retryDelayMs = 0;
                    try
                    {
                        await _connectionPacer.WaitAsync(token);
                        if (retry) _metrics.IncReconnects();
                        _lastError = null;
                        using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
                        await ConnectAndRunAsync(session, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        bool wasInWorld = _inWorld;
                        _lastError = error is OperationCanceledException
                            ? "Timeout while connecting or waiting for the login response."
                            : error.Message;
                        if (_connected) _metrics.IncDisconnects();
                        else _metrics.IncConnectionFailures();
                        _metrics.RecordError(_name, _lastError);
                        _permanentFailure = IsPermanentError(_lastError);
                        if (!_config.Reconnect || _permanentFailure) break;
                        failures = wasInWorld ? 1 : Math.Min(failures + 1, 5);
                        retryDelayMs = Math.Min(2000 * (1 << (failures - 1)), 30000);
                        if (error is LoginWaitException waiting)
                            retryDelayMs = Math.Max(retryDelayMs, waiting.RetrySeconds * 1000);
                        retryDelayMs += Random.Shared.Next(500, 1500);
                        retry = true;
                    }
                    finally { CleanupConnection(); }

                    if (retryDelayMs > 0)
                        await Task.Delay(retryDelayMs, token);
                    else break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                CleanupConnection();
                // Run owns these resources; no writer survives ConnectAndRunAsync.
                _writeLock.Dispose();
                _stop.Dispose();
            }
        }

        private static bool IsPermanentError(string message) =>
            message.Contains("password is not correct", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("account name or password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("account has been banned", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("character is not", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

        public void Stop()
        {
            try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Stop();
            if (Volatile.Read(ref _started) == 0)
            {
                _writeLock.Dispose();
                _stop.Dispose();
            }
        }

        private void CleanupConnection()
        {
            _inWorld = false;
            Stats = null;
            LastServerMessage = null;
            if (_connected)
            {
                _connected = false;
                _metrics.Disconnected();
            }
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
            _fightModesSent = false;
            _parser = null;
            _worldState.Clear();
            lock (_monsterLock)
            {
                _recentMonsters.Clear();
                _allSeenMonsters.Clear();
            }
        }

        private async Task ConnectAndRunAsync(CancellationTokenSource session, CancellationToken shutdownToken)
        {
            var token = session.Token;
            _client = new TcpClient { NoDelay = true };
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(5));
                await _client.ConnectAsync(_config.Host, _config.Port, connect.Token);
            }
            _stream = _client.GetStream();
            _connected = true;
            _metrics.Connected();
            _lastSend = _lastPing = 0;

            // Create structured parser for this session
            _parser = new Protocol860Parser(_worldState, _metrics);
            _parser.OnPingReceived = async () => await SendPingBackAsync(token);
            _parser.OnLoginAck = () => { _inWorld = true; };
            _parser.OnDisconnect = msg => throw new IOException(msg);
            _parser.OnDisconnectWait = (msg, retry) => throw new LoginWaitException(msg, retry);
            _parser.OnTextMessage = msg => { LastServerMessage = msg; };

            byte[] keyBytes = RandomNumberGenerator.GetBytes(16);
            for (int i = 0; i < 4; i++)
                _xteaKey[i] = BitConverter.ToUInt32(keyBytes, i * 4);

            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(30));
                byte[] challenge = await ReadMessageAsync(handshake.Token);
                if (challenge.Length != 12 ||
                    BitConverter.ToUInt32(challenge, 0) != Adler32(challenge.AsSpan(4)) ||
                    BitConverter.ToUInt16(challenge, 4) != 6 || challenge[6] != 0x1F)
                    throw new InvalidDataException("Invalid TFS 8.60 challenge.");

                await SendLoginMessageAsync(BitConverter.ToUInt32(challenge, 7), challenge[11], handshake.Token);
                // TFS can bundle stats from equipment loading BEFORE the 0x0A acknowledgement.
                while (!_inWorld)
                    await ProcessEncryptedPacketAsync(await ReadMessageAsync(handshake.Token), handshake.Token);
            }

            var tasks = new List<Task> { ReadLoopAsync(token), KeepAliveLoopAsync(token), IdleTurnLoopAsync(token) };
            if (!_config.LoginOnly)
            {
                if (_config.AiEnabled)
                {
                    tasks.Add(BrainLoopAsync(token));
                }
                else
                {
                    tasks.Add(WalkLoopAsync(token));
                    tasks.Add(ChatLoopAsync(token));
                    tasks.Add(SpellLoopAsync(token));
                    tasks.Add(AttackLoopAsync(token));
                }
            }
            try
            {
                await await Task.WhenAny(tasks);
            }
            finally
            {
                session.Cancel();
                // All old read/write/action loops finish before the next socket and key exist.
                try { await Task.WhenAll(tasks); } catch (Exception) { }
                if (shutdownToken.IsCancellationRequested && _inWorld)
                    await TryLogoutAsync();
            }
        }

        private async Task TryLogoutAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                var message = new OutputMessage();
                message.AddU8(0x14);
                await SendRawGameMessageAsync(message, timeout.Token);
            }
            catch (Exception) { /* The socket may already be closed. */ }
        }

        private async Task SendLoginMessageAsync(uint timestamp, byte random, CancellationToken token)
        {
            var rsa = new OutputMessage();
            rsa.AddU8(0);
            foreach (uint key in _xteaKey) rsa.AddU32(key);
            rsa.AddU8(0);
            rsa.AddString(_name);
            rsa.AddString(_name);
            rsa.AddString(_password);
            rsa.AddU32(timestamp);
            rsa.AddU8(random);
            byte[] raw = rsa.GetBuffer();
            if (raw.Length > 128)
                throw new InvalidDataException("Account name and password exceed the RSA login block.");
            byte[] padded = new byte[128];
            raw.CopyTo(padded, 0);

            var message = new OutputMessage();
            message.AddU8(0x0A);
            message.AddU16(2); // Windows/CIP: no custom-client ping sequence or extensions.
            message.AddU16(860);
            message.AddBytes(Rsa.Encrypt(padded));
            await WritePacketAsync(WrapChecksum(message.GetBuffer()), token);
        }

        private async Task<byte[]> ReadMessageAsync(CancellationToken token)
        {
            var stream = _stream ?? throw new IOException("Stream is closed.");
            byte[] header = new byte[2];
            try { await stream.ReadExactlyAsync(header, token); }
            catch (EndOfStreamException) { throw new IOException("Server closed the connection."); }
            int size = BitConverter.ToUInt16(header);
            if (size < 5) throw new InvalidDataException($"Invalid packet size: {size}.");
            byte[] body = new byte[size];
            await stream.ReadExactlyAsync(body, token);
            _metrics.IncPacketsIn();
            _metrics.AddBytesIn(size + 2);
            return body;
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
                await ProcessEncryptedPacketAsync(await ReadMessageAsync(token), token);
        }

        private async Task ProcessEncryptedPacketAsync(byte[] body, CancellationToken token)
        {
            if (body.Length < 12 || (body.Length - 4) % 8 != 0 ||
                BitConverter.ToUInt32(body, 0) != Adler32(body.AsSpan(4)))
                throw new InvalidDataException("Invalid encrypted packet length or Adler32 checksum.");
            byte[] encrypted = body.AsSpan(4).ToArray();
            Xtea.Decrypt(encrypted, _xteaKey);
            int length = BitConverter.ToUInt16(encrypted);
            if (length < 1 || length > encrypted.Length - 2)
                throw new InvalidDataException("Invalid XTEA payload length.");
            await ProcessPayloadAsync(new InputMessage(encrypted, 2, length + 2), token);
        }

        private Task ProcessPayloadAsync(InputMessage payload, CancellationToken token)
        {
            if (_parser == null) return Task.CompletedTask;

            bool becameInWorld = _parser.ProcessPayload(payload, _inWorld);
            if (becameInWorld)
            {
                _inWorld = true;
            }

            // Sync legacy Stats property from world state for backward compat
            var p = _worldState.Player;
            if (p.MaxHp > 0)
            {
                Stats = new PlayerStats(p.Hp, p.MaxHp, p.Mana, p.MaxMana, p.Level);
            }

            // Sync damage time from world state
            if (p.LastDamageTakenTime > LastDamageTakenTime)
                LastDamageTakenTime = p.LastDamageTakenTime;

            // Populate legacy monster lists from WorldState for backward compat
            if (_config.EnableAttack)
            {
                lock (_monsterLock)
                {
                    foreach (var creature in _worldState.GetVisibleMonsters())
                    {
                        _allSeenMonsters.Add(creature.Id);
                        if (!_recentMonsters.Contains(creature.Id))
                        {
                            _recentMonsters.Add(creature.Id);
                            if (_recentMonsters.Count > 10) _recentMonsters.RemoveAt(0);
                        }
                    }
                }
            }

            return Task.CompletedTask;
        }

        private async Task KeepAliveLoopAsync(CancellationToken token)
        {
            await Task.Delay(Random.Shared.Next(200, 1000), token);
            while (!token.IsCancellationRequested)
            {
                if (_inWorld) await SendPingBackAsync(token);
                await Task.Delay(TimeSpan.FromMilliseconds(_config.KeepAliveIntervalMs), token);
            }
        }

        private async Task IdleTurnLoopAsync(CancellationToken token)
        {
            if (_config.IdleTurnIntervalMs <= 0) { await Task.Delay(Timeout.Infinite, token); return; }
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(
                    _config.IdleTurnIntervalMs * (0.9 + Random.Shared.NextDouble() * 0.2)), token);
                if (!_inWorld) continue;
                var message = new OutputMessage();
                message.AddU8((byte)(0x6F + Random.Shared.Next(4)));
                await SendRawGameMessageAsync(message, token);
                _metrics.IncTurns();
            }
        }

        private async Task SendPingBackAsync(CancellationToken token)
        {
            var message = new OutputMessage();
            message.AddU8(0x1E);
            await SendRawGameMessageAsync(message, token, isPing: true);
        }

        private async Task SendRawGameMessageAsync(OutputMessage message, CancellationToken token, bool isPing = false)
        {
            var payload = message.GetBuffer();
            byte[] padded = new byte[(payload.Length + 2 + 7) / 8 * 8];
            BitConverter.TryWriteBytes(padded.AsSpan(), (ushort)payload.Length);
            payload.CopyTo(padded, 2);
            Xtea.Encrypt(padded, _xteaKey);
            await WritePacketAsync(WrapChecksum(padded), token, isPing);
        }

        private async Task WritePacketAsync(byte[] packet, CancellationToken token, bool isPing = false)
        {
            long queued = Stopwatch.GetTimestamp();
            await _writeLock.WaitAsync(token);
            try
            {
                if (isPing && _lastPing != 0 && Stopwatch.GetElapsedTime(_lastPing).TotalMilliseconds < 1000)
                    return;
                // Bound all writes, including heartbeats and actions, below TFS's 25 pps.
                if (_lastSend != 0)
                {
                    double remaining = 55 - Stopwatch.GetElapsedTime(_lastSend).TotalMilliseconds;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), token);
                }
                _metrics.AddQueueWaitMs(Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
                var stream = _stream ?? throw new IOException("Stream is closed.");
                long started = Stopwatch.GetTimestamp();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await stream.WriteAsync(packet, timeout.Token);
                _lastSend = Stopwatch.GetTimestamp();
                _metrics.AddDrainMs(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                _metrics.IncSent();
                _metrics.AddBytesOut(packet.Length);
                if (isPing)
                {
                    _lastPing = _lastSend;
                    _metrics.IncPingbacks();
                }
            }
            finally { _writeLock.Release(); }
        }

        private static byte[] WrapChecksum(byte[] payload)
        {
            var result = new byte[payload.Length + 6];
            BitConverter.TryWriteBytes(result.AsSpan(), (ushort)(payload.Length + 4));
            BitConverter.TryWriteBytes(result.AsSpan(2), Adler32(payload));
            payload.CopyTo(result, 6);
            return result;
        }

        private static uint Adler32(ReadOnlySpan<byte> data)
        {
            uint a = 1, b = 0;
            foreach (byte value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        private sealed class LoginWaitException(string message, byte retrySeconds) : IOException(message)
        {
            public int RetrySeconds { get; } = retrySeconds;
        }

        private async Task WalkLoopAsync(CancellationToken token)
        {
            if (!_config.EnableRandomWalk) { await Task.Delay(-1, token); return; }
            var rand = new Random();
            byte[] walks = { 0x65, 0x66, 0x67, 0x68 };
            await Task.Delay(rand.Next(100, 3000), token); // Initial spawn spread

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.WalkIntervalMs * 0.2)), (int)(_config.WalkIntervalMs * 0.2));
                await Task.Delay((int)_config.WalkIntervalMs + jitter, token);
                if (!_inWorld) continue;

                // Only pause random wandering if ChaseMode is ENFORCED AND we are actively fighting!
                if (_config.EnableChaseMode && (DateTime.UtcNow - LastAttackTime).TotalSeconds < 5) continue;
                
                var msg = new OutputMessage();
                msg.AddU8(walks[rand.Next(walks.Length)]);
                await SendRawGameMessageAsync(msg, token);
                _metrics.IncWalks();
                LastWalkTime = DateTime.UtcNow;
            }
        }

        // Humanized chat messages — short, varied, rare
        private static readonly string[] _chatMessages = {
            "hi", "hello", "lol", "kk", "kkk", "hmm", "go?", "brb",
            "mana", "heal", "gg", "xd", "nice", "thx", "ty", "lf pt",
            "anyone?", "afk", "back", "gl", "hf", "wb",
            "e ai", "opa", "vlw", "ss", "cya"
        };

        private async Task ChatLoopAsync(CancellationToken token)
        {
            if (!_config.EnableChat) { await Task.Delay(-1, token); return; }
            Random rand = new Random();
            await Task.Delay(rand.Next(5000, 30000), token); // Long initial delay

            while (!token.IsCancellationRequested)
            {
                // Long interval + big jitter = rare and desynchronized chat
                int baseInterval = Math.Max(15000, (int)_config.ChatIntervalMs);
                int jitter = rand.Next(baseInterval / 2, baseInterval * 2);
                await Task.Delay(baseInterval + jitter, token);
                if (!_inWorld) continue;

                // Only chat with a small chance per tick to avoid 500 bots talking at once
                if (rand.NextDouble() > 0.3) continue;

                string text = _chatMessages[rand.Next(_chatMessages.Length)];
                var msg = new OutputMessage();
                msg.AddU8(0x96); // TALK
                msg.AddU8(1);    // SAY
                msg.AddString(text);
                await SendRawGameMessageAsync(msg, token);
                _metrics.IncChats();
            }
        }

        private async Task SpellLoopAsync(CancellationToken token)
        {
            var voc = _config.VocationConfig;
            var slots = new[] { voc.Spell1, voc.Spell2, voc.Spell3, voc.Spell4 }
                .Where(s => s.Enabled && !string.IsNullOrEmpty(s.SpellText))
                .ToArray();

            // Fallback: se não há slots configurados, usa o SpellText legacy
            if (slots.Length == 0)
            {
                if (!_config.EnableSpell) { await Task.Delay(-1, token); return; }
                slots = new[] { new SpellSlot { Enabled = true, SpellText = _config.SpellText, IntervalMs = (int)_config.SpellIntervalMs } };
            }

            var rand = new Random();
            var lastCast = new DateTime[slots.Length];
            await Task.Delay(rand.Next(500, 3000), token);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(500, token); // tick rate
                if (!_inWorld) continue;

                // ── RULE: Only cast offensive spells when there is a VALID target ──
                // Check WorldState for a valid target first
                uint currentTarget = _worldState.Player.CurrentTargetId;
                bool hasValidTarget = false;

                if (currentTarget != 0)
                {
                    var target = _worldState.GetCreature(currentTarget);
                    hasValidTarget = target != null &&
                                     target.Visible &&
                                     target.HealthPercent > 0 &&
                                     target.Z == _worldState.Player.Z;
                }

                // Also check if we recently sent an attack (fallback for when
                // WorldState target tracking hasn't caught up yet)
                if (!hasValidTarget && (DateTime.UtcNow - LastAttackTime).TotalSeconds > 3)
                    continue; // No target, no spell

                // Check mana availability from WorldState
                double manaPercent = _worldState.Player.ManaPercent;

                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if ((DateTime.UtcNow - lastCast[i]).TotalMilliseconds < slot.IntervalMs) continue;

                    // Respect MinManaPercent — don't cast if mana is too low
                    if (manaPercent < slot.MinManaPercent) continue;

                    var msg = new OutputMessage();
                    msg.AddU8(0x96); // TALK
                    msg.AddU8(1);
                    msg.AddString(slot.SpellText);
                    await SendRawGameMessageAsync(msg, token);
                    _metrics.IncSpells();
                    lastCast[i] = DateTime.UtcNow;
                    LastSpellTime = DateTime.UtcNow;
                    break; // uma spell por tick para não spammar
                }
            }
        }

        // Current attack target — maintained across ticks
        private uint _currentAttackTarget;
        private DateTime _targetAcquiredTime = DateTime.MinValue;
        private DateTime _lastAttackSentTime = DateTime.MinValue;

        private async Task AttackLoopAsync(CancellationToken token)
        {
            if (!_config.EnableAttack) { await Task.Delay(-1, token); return; }
            Random rand = new Random();
            await Task.Delay(rand.Next(1000, 4000), token);

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.AttackScanIntervalMs * 0.2)), (int)(_config.AttackScanIntervalMs * 0.2));
                await Task.Delay((int)_config.AttackScanIntervalMs + jitter, token);
                if (!_inWorld) continue;

                // Send fight modes once per session
                if (!_fightModesSent)
                {
                    var modeMsg = Protocol860Writer.FightModes(
                        _config.FightMode,
                        (byte)(_config.EnableChaseMode ? 1 : 0),
                        (byte)(_config.SafeFight ? 1 : 0));
                    await SendRawGameMessageAsync(modeMsg, token);
                    _fightModesSent = true;
                }

                // ── TARGET VALIDATION ──
                // Check if current target is still valid
                bool targetValid = false;
                if (_currentAttackTarget != 0)
                {
                    var target = _worldState.GetCreature(_currentAttackTarget);
                    targetValid = target != null &&
                                  target.Visible &&
                                  target.HealthPercent > 0 &&
                                  target.Z == _worldState.Player.Z &&
                                  target.Type == CreatureType.Monster;
                }

                // ── TARGET ACQUISITION ──
                // If no valid target, find the best one from WorldState
                if (!targetValid)
                {
                    _currentAttackTarget = 0;
                    _worldState.Player.CurrentTargetId = 0;

                    // Find closest visible monster on same floor with HP > 0
                    uint bestId = 0;
                    int bestDist = int.MaxValue;
                    var px = _worldState.Player.X;
                    var py = _worldState.Player.Y;

                    foreach (var monster in _worldState.GetVisibleMonsters())
                    {
                        int dist = monster.ChebyshevDistanceTo(px, py);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestId = monster.Id;
                        }
                    }

                    if (bestId != 0)
                    {
                        _currentAttackTarget = bestId;
                        _worldState.Player.CurrentTargetId = bestId;
                        _targetAcquiredTime = DateTime.UtcNow;
                    }
                }

                // ── SEND ATTACK ──
                if (_currentAttackTarget != 0)
                {
                    var attackMsg = Protocol860Writer.Attack(_currentAttackTarget);
                    await SendRawGameMessageAsync(attackMsg, token);
                    _metrics.IncAttacks();
                    LastAttackTime = DateTime.UtcNow;
                    _lastAttackSentTime = DateTime.UtcNow;
                }
            }
        }

        private async Task BrainLoopAsync(CancellationToken token)
        {
            int seed = _config.RandomSeed.HasValue
                ? _config.RandomSeed.Value ^ _name.GetHashCode()
                : _name.GetHashCode() ^ Environment.TickCount;

            _brain = new BotBrain(_worldState, _config, seed);

            var rng = new Random(seed);
            // Stagger initial start so bots don't all tick on the exact same millisecond
            await Task.Delay(rng.Next(200, 1500), token);

            while (!token.IsCancellationRequested)
            {
                // Tick interval: 150-250ms + light jitter
                int jitter = rng.Next(-25, 26);
                await Task.Delay(200 + jitter, token);

                if (!_inWorld) continue;

                var actionMsg = _brain.Tick();
                if (actionMsg != null)
                {
                    await SendRawGameMessageAsync(actionMsg, token);

                    // Track metrics based on action sent
                    byte opcode = actionMsg.GetBuffer()[0];
                    if (opcode >= 0x65 && opcode <= 0x6D)
                    {
                        _metrics.IncWalks();
                        LastWalkTime = DateTime.UtcNow;
                    }
                    else if (opcode == 0x64) // autowalk
                    {
                        _metrics.IncWalks();
                        LastWalkTime = DateTime.UtcNow;
                    }
                    else if (opcode == 0xA1) // attack
                    {
                        _metrics.IncAttacks();
                        LastAttackTime = DateTime.UtcNow;
                    }
                    else if (opcode == 0x96) // say (spell, heal, or chat)
                    {
                        _metrics.IncSpells();
                        LastSpellTime = DateTime.UtcNow;
                    }
                }
            }
        }
    }
}
