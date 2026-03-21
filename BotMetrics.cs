using System.Threading;

namespace StressBotBenchmark
{
    public class BotMetrics
    {
        public int ConnectedCount => 1; // Evaluated dynamically usually
        
        private int _enqueued;
        private int _sent;
        private int _dropped;
        private int _queueFull;
        private int _pingbacks;
        private int _walks;
        private int _chats;
        private int _spells;
        private int _attacks;
        private int _reconnects;
        private int _disconnects;
        private int _packetsIn;
        private long _bytesIn;
        private long _bytesOut;

        private double _drainMsSum;
        private int _drainSamples;
        private double _maxSendLagMs;
        private readonly object _lagLock = new object();
        
        public int Enqueued => _enqueued;
        public int Sent => _sent;
        public int Dropped => _dropped;
        public int QueueFull => _queueFull;
        public int Pingbacks => _pingbacks;
        public int Walks => _walks;
        public int Chats => _chats;
        public int Spells => _spells;
        public int Attacks => _attacks;
        public int Reconnects => _reconnects;
        public int Disconnects => _disconnects;
        public int PacketsIn => _packetsIn;
        public long BytesIn => _bytesIn;
        public long BytesOut => _bytesOut;

        public double AvgDrainMs => _drainSamples > 0 ? _drainMsSum / _drainSamples : 0;
        public double MaxSendLagMs => _maxSendLagMs;

        public void IncEnqueued() => Interlocked.Increment(ref _enqueued);
        public void IncSent() => Interlocked.Increment(ref _sent);
        public void IncDropped() => Interlocked.Increment(ref _dropped);
        public void IncQueueFull() => Interlocked.Increment(ref _queueFull);
        public void IncPingbacks() => Interlocked.Increment(ref _pingbacks);
        public void IncWalks() => Interlocked.Increment(ref _walks);
        public void IncChats() => Interlocked.Increment(ref _chats);
        public void IncSpells() => Interlocked.Increment(ref _spells);
        public void IncAttacks() => Interlocked.Increment(ref _attacks);
        public void IncReconnects() => Interlocked.Increment(ref _reconnects);
        public void IncDisconnects() => Interlocked.Increment(ref _disconnects);
        public void IncPacketsIn() => Interlocked.Increment(ref _packetsIn);
        public void AddBytesIn(long b) => Interlocked.Add(ref _bytesIn, b);
        public void AddBytesOut(long b) => Interlocked.Add(ref _bytesOut, b);

        public void AddDrainMs(double ms)
        {
            double oldSum, newSum;
            do { oldSum = _drainMsSum; newSum = oldSum + ms; } 
            while (Interlocked.CompareExchange(ref _drainMsSum, newSum, oldSum) != oldSum);
            
            Interlocked.Increment(ref _drainSamples);

            lock (_lagLock)
            {
                if (ms > _maxSendLagMs) _maxSendLagMs = ms;
            }
        }
    }
}
