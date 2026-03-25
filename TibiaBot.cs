using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StressBotBenchmark.Network;

namespace StressBotBenchmark
{
    public class TibiaBot : IDisposable
    {
        private const int MaxPacketSize = 65535; // Tibia max packet
        private const int ConnectTimeoutMs = 5000;

        private readonly string _name;
        private readonly string _password;
        private readonly BotConfig _config;
        private readonly BotMetrics _metrics;
        private readonly uint[] _xteaKey = new uint[4];
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private volatile bool _inWorld = false;
        private volatile bool _disposed = false;
        private DateTime _lastPingbackTime = DateTime.MinValue;
        private int _reconnectAttempts = 0;
        private string? _lastError = null;
        private bool _permanentFailure = false;

        public TibiaBot(string name, string password, BotConfig config, BotMetrics metrics)
        {
            _name = name;
            _password = password;
            _config = config;
            _metrics = metrics;
            var rand = new Random();
            for (int i = 0; i < 4; i++) _xteaKey[i] = (uint)rand.Next();
        }


        public string Name => _name;
        public bool InWorld => _inWorld;

        public DateTime LastWalkTime { get; private set; } = DateTime.MinValue;
        public DateTime LastSpellTime { get; private set; } = DateTime.MinValue;
        public DateTime LastAttackTime { get; private set; } = DateTime.MinValue;
        public DateTime LastDamageTakenTime { get; private set; } = DateTime.MinValue;
        public string? LastError => _lastError;
        public bool PermanentFailure => _permanentFailure;

        public int TrackedMonstersTotal => _allSeenMonsters.Count;

        private bool _running = false;

        public async Task StartAsync()
        {
            _running = true;
            var rand = new Random();
            while (_running && !_disposed)
            {
                _cts = new CancellationTokenSource();
                try
                {
                    await ConnectAndRunAsync(_cts.Token);
                    _reconnectAttempts = 0;
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    // Se _lastError já contém erro do servidor (ex: auth fail via encrypted packet),
                    // usar ele. Senão usar e.Message (erro de conexão/stream).
                    string errorMsg = _lastError ?? e.Message;
                    _lastError = errorMsg;

                    // Erros permanentes: não adianta reconectar
                    if (IsPermanentError(errorMsg))
                    {
                        _permanentFailure = true;
                        _metrics.IncDisconnects();
                        break;
                    }

                    if (!_config.Reconnect) break;
                    _metrics.IncDisconnects();
                    _metrics.IncReconnects();
                    _reconnectAttempts++;
                    int backoffMs = Math.Min(2000 * (1 << Math.Min(_reconnectAttempts - 1, 4)), 30000);
                    int jitter = rand.Next(0, backoffMs / 2);
                    try { await Task.Delay(backoffMs + jitter); } catch { }
                }
                finally
                {
                    CleanupConnection();
                }
            }
        }

        private static bool IsPermanentError(string message)
        {
            // Erros que o servidor envia e que nunca vão mudar com retry
            return message.Contains("password is not correct", StringComparison.OrdinalIgnoreCase)
                || message.Contains("account name or password", StringComparison.OrdinalIgnoreCase)
                || message.Contains("account has been banned", StringComparison.OrdinalIgnoreCase)
                || message.Contains("character is not", StringComparison.OrdinalIgnoreCase)
                || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            CleanupConnection();
        }

        private void CleanupConnection()
        {
            _inWorld = false;
            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _stream = null;
            _client = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            _writeLock.Dispose();
        }

        private async Task ConnectAndRunAsync(CancellationToken token)
        {
            _lastError = null; // Reset antes de cada tentativa

            _client = new TcpClient
            {
                NoDelay = true,
                SendTimeout = 5000,
                ReceiveTimeout = 30000
            };

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            connectCts.CancelAfter(ConnectTimeoutMs);
            await _client.ConnectAsync(_config.Host, _config.Port, connectCts.Token);
            _stream = _client.GetStream();
            _inWorld = false;

            byte[] challengeMsg;
            try
            {
                challengeMsg = await ReadMessageAsync(token);
            }
            catch (EndOfStreamException)
            {
                throw new Exception("Server closed connection before sending challenge (connection limit? server not running?)");
            }

            if (challengeMsg.Length < 12 || challengeMsg[6] != 0x1F)
            {
                // Maybe server sent a disconnect message (unencrypted 0x14)
                if (challengeMsg.Length >= 4 && challengeMsg[0] == 0x14)
                {
                    var errMsg = new InputMessage(challengeMsg, 1, challengeMsg.Length);
                    throw new Exception($"Server rejected (pre-login): {errMsg.GetString()}");
                }
                string hex = BitConverter.ToString(challengeMsg, 0, Math.Min(challengeMsg.Length, 20));
                throw new Exception($"Invalid challenge (len={challengeMsg.Length}, hex={hex})");
            }
            
            uint ts = BitConverter.ToUInt32(challengeMsg, 7);
            byte rand = challengeMsg[11];

            await SendLoginMessageAsync(ts, rand, token);

            // Read first response - could be encrypted game data or disconnect
            byte[] firstResponse;
            try
            {
                firstResponse = await ReadMessageAsync(token);
            }
            catch (EndOfStreamException)
            {
                throw new Exception("Server closed connection after login (RSA decrypt failed? wrong protocol version?)");
            }

            // Try to parse first response - check for unencrypted disconnect (0x14)
            if (firstResponse.Length >= 4 && firstResponse[0] == 0x14)
            {
                var errMsg = new InputMessage(firstResponse, 1, firstResponse.Length);
                throw new Exception($"Server rejected: {errMsg.GetString()}");
            }

            // Process first encrypted response normally
            ProcessEncryptedPacket(firstResponse);

            var readTask = ReadLoopAsync(token);

            if (_config.LoginOnly)
            {
                // Login-only mode: just maintain connection and respond to pings
                await readTask;
            }
            else
            {
                var walkTask = WalkLoopAsync(token);
                var chatTask = ChatLoopAsync(token);
                var spellTask = SpellLoopAsync(token);
                var attackTask = AttackLoopAsync(token);

                Task completedTask = await Task.WhenAny(readTask, walkTask, chatTask, spellTask, attackTask);
                _cts?.Cancel();
                await completedTask;
            }
        }

        private void ProcessEncryptedPacket(byte[] body)
        {
            if (body.Length < 6) return;
            byte[] encrypted = new byte[body.Length - 4];
            Array.Copy(body, 4, encrypted, 0, encrypted.Length);
            if (encrypted.Length % 8 != 0) return;
            Xtea.Decrypt(encrypted, _xteaKey);

            var msg = new InputMessage(encrypted);
            ushort innerLen = msg.GetU16();
            int end = Math.Min(msg.Position + innerLen, encrypted.Length);
            var payload = new InputMessage(encrypted, msg.Position, end);

            // Check for disconnect opcode
            if (payload.Remaining > 0)
            {
                int savedPos = payload.Position;
                byte op = payload.GetU8();
                if (op == 0x14)
                {
                    string reason = payload.GetString();
                    _lastError = reason;
                }
            }
        }

        private async Task SendLoginMessageAsync(uint ts, byte rand, CancellationToken token)
        {
            // Protocolo Tibia 8.60 / TFS 1.8
            var rsaBytes = new OutputMessage();
            rsaBytes.AddU8(0);                                    // RSA check byte
            for (int i = 0; i < 4; i++) rsaBytes.AddU32(_xteaKey[i]); // XTEA key
            rsaBytes.AddU8(0);                                    // gamemaster flag (0 = jogador normal)
            rsaBytes.AddString(_name);                            // account name
            rsaBytes.AddString(_name);                            // character name (igual ao account)
            rsaBytes.AddString(_password);                        // password
            rsaBytes.AddU32(ts);                                  // challenge timestamp
            rsaBytes.AddU8(rand);                                 // challenge random

            byte[] rawRsa = rsaBytes.GetBuffer();
            byte[] paddedRsa = new byte[128];
            Array.Copy(rawRsa, paddedRsa, rawRsa.Length);
            byte[] encryptedRsa = Rsa.Encrypt(paddedRsa);

            var msg = new OutputMessage();
            msg.AddU16(2);    // OS: 2 = Windows
            msg.AddU16(860);  // Protocol version 8.60
            msg.AddBytes(encryptedRsa);

            var payload = msg.GetBuffer();
            byte[] final = new byte[payload.Length + 3];
            final[0] = (byte)((payload.Length + 1) & 0xFF);
            final[1] = (byte)(((payload.Length + 1) >> 8) & 0xFF);
            final[2] = 0x0A; // Game protocol ID
            Array.Copy(payload, 0, final, 3, payload.Length);
            
            _metrics.AddBytesOut(final.Length);
            _metrics.IncSent();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _stream!.WriteAsync(final, token);
            sw.Stop();
            _metrics.AddDrainMs(sw.Elapsed.TotalMilliseconds);
        }



        private async Task<byte[]> ReadMessageAsync(CancellationToken token)
        {
            var stream = _stream ?? throw new InvalidOperationException("Stream closed");
            byte[] sizeHeader = new byte[2];
            int read = 0;
            while (read < 2)
            {
                int r = await stream.ReadAsync(sizeHeader, read, 2 - read, token);
                if (r == 0) throw new EndOfStreamException();
                read += r;
            }
            int size = sizeHeader[0] | (sizeHeader[1] << 8);
            if (size <= 0 || size > MaxPacketSize)
                throw new InvalidDataException($"Invalid packet size: {size}");

            byte[] body = new byte[size];
            read = 0;
            while (read < size)
            {
                int r = await stream.ReadAsync(body, read, size - read, token);
                if (r == 0) throw new EndOfStreamException();
                read += r;
            }
            _metrics.IncPacketsIn();
            _metrics.AddBytesIn((long)size + 2);
            return body;
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                byte[] body = await ReadMessageAsync(token);
                if (body.Length < 6) continue;
                
                byte[] encrypted = new byte[body.Length - 4];
                Array.Copy(body, 4, encrypted, 0, encrypted.Length);
                
                if (encrypted.Length % 8 != 0) continue;
                Xtea.Decrypt(encrypted, _xteaKey);
                
                var msg = new InputMessage(encrypted);
                ushort innerLen = msg.GetU16();
                int end = Math.Min(msg.Position + innerLen, encrypted.Length);
                var payload = new InputMessage(encrypted, msg.Position, end);
                
                await ProcessPayloadAsync(payload, token);
            }
        }

        private bool _fightModesSent = false;
        private readonly List<uint> _recentMonsters = new();
        private readonly HashSet<uint> _allSeenMonsters = new();
        private readonly object _monsterLock = new();

        private async Task ProcessPayloadAsync(InputMessage payload, CancellationToken token)
        {
            if (!_inWorld)
            {
                _inWorld = true;
                _fightModesSent = false;
            }

            // Only run heuristic scanners when their features are actually enabled.
            // This avoids wasting CPU scanning every packet when attack/engagement is off.
            if (_config.EnableAttack)
            {
                int length = payload.Remaining;
                int offset = payload.Position;
                byte[] buf = payload.Buffer;

                // Monster ID heuristic scanner
                for (int i = 0; i <= length - 4; i++)
                {
                    uint val = BitConverter.ToUInt32(buf, offset + i);
                    if (val >= 0x40000000 && val <= 0x401FFFFF)
                    {
                        lock (_monsterLock)
                        {
                            _allSeenMonsters.Add(val);
                            if (!_recentMonsters.Contains(val))
                            {
                                _recentMonsters.Add(val);
                                if (_recentMonsters.Count > 10) _recentMonsters.RemoveAt(0);
                            }
                        }
                    }
                }

                // Damage heuristic scanner
                for (int i = 0; i <= length - 11; i++)
                {
                    if (buf[offset + i] == 89 && buf[offset + i + 1] == 111 && buf[offset + i + 2] == 117 &&
                        buf[offset + i + 3] == 32 && buf[offset + i + 4] == 108 && buf[offset + i + 5] == 111 &&
                        buf[offset + i + 6] == 115 && buf[offset + i + 7] == 101 && buf[offset + i + 8] == 32)
                    {
                        LastDamageTakenTime = DateTime.UtcNow;
                    }
                    if (buf[offset + i] == 121 && buf[offset + i + 1] == 111 && buf[offset + i + 2] == 117 &&
                        buf[offset + i + 3] == 114 && buf[offset + i + 4] == 32 && buf[offset + i + 5] == 97 &&
                        buf[offset + i + 6] == 116 && buf[offset + i + 7] == 116 && buf[offset + i + 8] == 97 &&
                        buf[offset + i + 9] == 99 && buf[offset + i + 10] == 107)
                    {
                        LastAttackTime = DateTime.UtcNow;
                    }
                }
            }

            // Parse only the first opcode (no full protocol parser).
            if (payload.Remaining > 0)
            {
                byte op = payload.GetU8();
                if (op == 0x14)
                {
                    string reason = payload.GetString();
                    _lastError = reason;
                    return;
                }
                if (op == 0x1D)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastPingbackTime).TotalMilliseconds >= _config.PingbackMinIntervalMs)
                    {
                        _lastPingbackTime = now;
                        await SendPingBackAsync(token);
                    }
                }
            }
        }

        private async Task SendPingBackAsync(CancellationToken token)
        {
            var msg = new OutputMessage();
            msg.AddU8(0x1E); // PINGBACK
            await SendRawGameMessageAsync(msg, token);
            _metrics.IncPingbacks();
        }

        private async Task SendRawGameMessageAsync(OutputMessage msg, CancellationToken token)
        {
            byte[] inner = msg.GetBuffer();
            var innerWrapper = new OutputMessage();
            innerWrapper.AddU16((ushort)inner.Length);
            innerWrapper.AddBytes(inner);
            byte[] toEncrypt = innerWrapper.GetBuffer();

            int pad = (8 - toEncrypt.Length % 8) % 8;
            byte[] padded = new byte[toEncrypt.Length + pad];
            Array.Copy(toEncrypt, padded, toEncrypt.Length);
            Xtea.Encrypt(padded, _xteaKey);

            byte[] packet = new byte[padded.Length + 6];
            packet[0] = (byte)((padded.Length + 4) & 0xFF);
            packet[1] = (byte)(((padded.Length + 4) >> 8) & 0xFF);
            
            uint checksum = Adler32(padded);
            packet[2] = (byte)(checksum & 0xFF);
            packet[3] = (byte)((checksum >> 8) & 0xFF);
            packet[4] = (byte)((checksum >> 16) & 0xFF);
            packet[5] = (byte)((checksum >> 24) & 0xFF);
            
            Array.Copy(padded, 0, packet, 6, padded.Length);

            await _writeLock.WaitAsync(token);
            try
            {
                var stream = _stream;
                if (stream == null || !(_client?.Connected ?? false)) return;
                await stream.WriteAsync(packet, token);
                _metrics.IncSent();
                _metrics.AddBytesOut(packet.Length);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte val in data)
            {
                a = (a + val) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
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

        private async Task ChatLoopAsync(CancellationToken token)
        {
            if (!_config.EnableChat) { await Task.Delay(-1, token); return; }
            Random rand = new Random();
            await Task.Delay(rand.Next(500, 4000), token);

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.ChatIntervalMs * 0.2)), (int)(_config.ChatIntervalMs * 0.2));
                await Task.Delay((int)_config.ChatIntervalMs + jitter, token);
                if (!_inWorld) continue;
                
                var msg = new OutputMessage();
                msg.AddU8(0x96); // TALK
                msg.AddU8(1);
                msg.AddString($"Hello_im_csharp_bot_{_name}");
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
            await Task.Delay(rand.Next(200, 2000), token);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(500, token); // tick rate
                if (!_inWorld) continue;

                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if ((DateTime.UtcNow - lastCast[i]).TotalMilliseconds < slot.IntervalMs) continue;

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

                if (!_fightModesSent)
                {
                    var modeMsg = new OutputMessage();
                    modeMsg.AddU8(0xA0); // CHANGE FIGHT MODES
                    modeMsg.AddU8(_config.FightMode); // 1 = Offensive, etc
                    modeMsg.AddU8((byte)(_config.EnableChaseMode ? 1 : 0)); // 1 = Chase, 0 = Stand
                    modeMsg.AddU8((byte)(_config.SafeFight ? 1 : 0));
                    await SendRawGameMessageAsync(modeMsg, token);
                    _fightModesSent = true;
                }

                uint targetId = 0;
                lock (_monsterLock)
                {
                    if (_recentMonsters.Count > 0)
                    {
                        targetId = _recentMonsters[rand.Next(_recentMonsters.Count)];
                    }
                }

                if (targetId != 0)
                {
                    var attackMsg = new OutputMessage();
                    attackMsg.AddU8(0xA1); // ATTACK
                    attackMsg.AddU32(targetId);
                    await SendRawGameMessageAsync(attackMsg, token);
                    _metrics.IncAttacks();
                }
            }
        }
    }
}
