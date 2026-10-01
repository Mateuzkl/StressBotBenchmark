using System.Threading;

namespace StressBotBenchmark
{
    public class BotMetrics
    {
        private int _connected;
        private int _connectionFailures;
        private int _turns;
        private string? _lastError;
        private int _parserErrors;
        private int _unknownOpcodes;
        private readonly int[] _unknownByOpcode = new int[256];
        private string? _lastParserError;
        public string? LastParserError => Volatile.Read(ref _lastParserError);
        public string UnknownSummary => string.Join(",", Enumerable.Range(0, 256)
            .Where(i => Volatile.Read(ref _unknownByOpcode[i]) != 0)
            .OrderByDescending(i => Volatile.Read(ref _unknownByOpcode[i])).Take(8)
            .Select(i => $"0x{i:X2}:{Volatile.Read(ref _unknownByOpcode[i])}"));
        public void RecordParserError(byte opcode, string error) => Volatile.Write(ref _lastParserError, $"0x{opcode:X2}: {error}");
        public void RecordUnknown(byte opcode) => Interlocked.Increment(ref _unknownByOpcode[opcode]);
        public int ParserErrors => Volatile.Read(ref _parserErrors);
        public int UnknownOpcodes => Volatile.Read(ref _unknownOpcodes);
        public void IncParserErrors() => Interlocked.Increment(ref _parserErrors);
        public void IncUnknownOpcodes() => Interlocked.Increment(ref _unknownOpcodes);
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
        private int _outfits;
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
        // Fixed-size cumulative histograms; no per-packet allocations or unbounded samples.
        private static readonly double[] LagBounds = { 0.1, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2000, 5000, double.PositiveInfinity };
        private readonly long[] _queueHistogram = new long[LagBounds.Length];
        private readonly long[] _sendHistogram = new long[LagBounds.Length];

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
        public int Outfits => Volatile.Read(ref _outfits);
        public int Actions => Walks + Attacks + Spells + Heals + Potions + Chats + Turns + Outfits;
        public int Reconnects => _reconnects;
        public int Disconnects => _disconnects;
        public int PacketsIn => _packetsIn;
        public long BytesIn => _bytesIn;
        public long BytesOut => _bytesOut;

        public double AvgDrainMs { get { lock (_lagLock) return _drainSamples > 0 ? _drainMsSum / _drainSamples : 0; } }
        public double MaxSendLagMs { get { lock (_lagLock) return _maxSendLagMs; } }
        public double AvgQueueWaitMs { get { lock (_lagLock) return _queueWaitSamples > 0 ? _queueWaitMsSum / _queueWaitSamples : 0; } }
        public double QueueP95Ms { get { lock (_lagLock) return Percentile(_queueHistogram, _queueWaitSamples, 0.95); } }
        public double QueueP99Ms { get { lock (_lagLock) return Percentile(_queueHistogram, _queueWaitSamples, 0.99); } }
        public double SendP95Ms { get { lock (_lagLock) return Percentile(_sendHistogram, _drainSamples, 0.95); } }
        public double SendP99Ms { get { lock (_lagLock) return Percentile(_sendHistogram, _drainSamples, 0.99); } }
        private static void AddSample(long[] histogram, double ms)
        {
            for (int i = 0; i < LagBounds.Length; i++)
                if (ms <= LagBounds[i]) { histogram[i]++; return; }
        }
        private static double Percentile(long[] histogram, long count, double percentile)
        {
            if (count == 0) return 0;
            long threshold = (long)Math.Ceiling(count * percentile), seen = 0;
            for (int i = 0; i < histogram.Length; i++)
                if ((seen += histogram[i]) >= threshold) return LagBounds[i];
            return double.PositiveInfinity;
        }
        public void AddQueueWaitMs(double ms)
        {
            lock (_lagLock) { _queueWaitMsSum += ms; _queueWaitSamples++; AddSample(_queueHistogram, ms); }
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
        public void IncOutfits() => Interlocked.Increment(ref _outfits);
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
                AddSample(_sendHistogram, ms);
                if (ms > _maxSendLagMs) _maxSendLagMs = ms;
            }
        }
    }
}
