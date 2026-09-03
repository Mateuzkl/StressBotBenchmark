using System.Diagnostics;

namespace StressBotBenchmark;

// Shared by initial logins AND reconnects. Reserve at execution time so a slow
// scheduler cannot release several overdue connection attempts in one burst.
public sealed class ConnectionPacer(double intervalMs) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long lastAttempt;

    public async Task WaitAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (lastAttempt != 0)
            {
                double remaining = intervalMs - Stopwatch.GetElapsedTime(lastAttempt).TotalMilliseconds;
                if (remaining > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(remaining), token);
            }
            token.ThrowIfCancellationRequested();
            lastAttempt = Stopwatch.GetTimestamp();
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
