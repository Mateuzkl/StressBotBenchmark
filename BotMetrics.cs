using System.Threading;

namespace StressBotBenchmark
{
    public class BotMetrics
    {
        private int _connected;
        private int _connectionFailures;
        private int _turns;
        private string? _lastError;
        public int ConnectedCount => Volatile.Read(ref _connected);
        public int ConnectionFailures => Volatile.Read(ref _connectionFailures);
        public int Turns => Volatile.Read(ref _turns);
        public string? LastError => Volatile.Read(ref _lastError);
        public void Connected() => Interlocked.Increment(ref _connected);
        public void Disconnected() => Interlocked.Decrement(ref _connected);
        public void IncConnectionFailures() => Interlocked.Increment(ref _connectionFailures);
        public void IncTurns() => Interlocked.Increment(ref _turns);
        public void RecordError(string bot, string error) => Volatile.Write(ref _lastError, $"{bot}: {error}");
        
        private int _enqueued;
        private int _sent;
        private int _dropped;
        private int _queueFull;
        private int _pingbacks;
        private int _walks;
        private int _chats;
        private int _spells;
        private int _attacks;
        private int _heals;
        private int _potions;
        private int _reconnects;
        private int _disconnects;
        private int _packetsIn;
        private long _bytesIn;
        private long _bytesOut;

        private double _drainMsSum;
        private int _drainSamples;
        private double _maxSendLagMs;
        private readonly object _lagLock = new object();
        private double _queueWaitMsSum;
        private long _queueWaitSamples;
        
        public int Enqueued => _enqueued;
        public int Sent => _sent;
        public int Dropped => _dropped;
        public int QueueFull => _queueFull;
        public int Pingbacks => _pingbacks;
        public int Walks => _walks;
        public int Chats => _chats;
        public int Spells => _spells;
        public int Attacks => _attacks;
        public int Heals => _heals;
        public int Potions => _potions;
        public int Reconnects => _reconnects;
        public int Disconnects => _disconnects;
        public int PacketsIn => _packetsIn;
        public long BytesIn => _bytesIn;
        public long BytesOut => _bytesOut;

        public double AvgDrainMs { get { lock (_lagLock) return _drainSamples > 0 ? _drainMsSum / _drainSamples : 0; } }
        public double MaxSendLagMs { get { lock (_lagLock) return _maxSendLagMs; } }
        public double AvgQueueWaitMs { get { lock (_lagLock) return _queueWaitSamples > 0 ? _queueWaitMsSum / _queueWaitSamples : 0; } }
        public void AddQueueWaitMs(double ms)
        {
            lock (_lagLock) { _queueWaitMsSum += ms; _queueWaitSamples++; }
        }

        public void IncEnqueued() => Interlocked.Increment(ref _enqueued);
        public void IncSent() => Interlocked.Increment(ref _sent);
        public void IncDropped() => Interlocked.Increment(ref _dropped);
        public void IncQueueFull() => Interlocked.Increment(ref _queueFull);
        public void IncPingbacks() => Interlocked.Increment(ref _pingbacks);
        public void IncWalks() => Interlocked.Increment(ref _walks);
        public void IncChats() => Interlocked.Increment(ref _chats);
        public void IncSpells() => Interlocked.Increment(ref _spells);
        public void IncAttacks() => Interlocked.Increment(ref _attacks);
        public void IncHeals() => Interlocked.Increment(ref _heals);
        public void IncPotions() => Interlocked.Increment(ref _potions);
        public void IncReconnects() => Interlocked.Increment(ref _reconnects);
        public void IncDisconnects() => Interlocked.Increment(ref _disconnects);
        public void IncPacketsIn() => Interlocked.Increment(ref _packetsIn);
        public void AddBytesIn(long b) => Interlocked.Add(ref _bytesIn, b);
        public void AddBytesOut(long b) => Interlocked.Add(ref _bytesOut, b);

        public void AddDrainMs(double ms)
        {
            lock (_lagLock)
            {
                _drainMsSum += ms;
                _drainSamples++;
                if (ms > _maxSendLagMs) _maxSendLagMs = ms;
            }
        }
    }
}
