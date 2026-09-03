using StressBotBenchmark.World;

namespace StressBotBenchmark.Navigation;

/// <summary>
/// Calculates realistic movement timing and decides whether to send single step or autowalk.
/// </summary>
public sealed class MovementPlanner
{
    private DateTime _lastMoveSent = DateTime.MinValue;
    private int _estimatedStepDurationMs = 300;

    /// <summary>
    /// Calculate estimated duration for a step based on player speed.
    /// TFS standard: step duration roughly 1000 * 100 / (speed * 2) or ~200-400ms.
    /// </summary>
    public int CalculateStepDurationMs(ushort speed)
    {
        if (speed == 0) speed = 200;
        // Formula approximated for standard Tibia ground (speed factor ~100-150)
        int ms = (int)(1000.0 * 100.0 / Math.Max(50, (int)speed));
        return Math.Clamp(ms, 150, 800);
    }

    /// <summary>
    /// Checks whether the bot is ready to move again without packet flooding.
    /// </summary>
    public bool CanMove(ushort speed)
    {
        _estimatedStepDurationMs = CalculateStepDurationMs(speed);
        return (DateTime.UtcNow - _lastMoveSent).TotalMilliseconds >= (_estimatedStepDurationMs * 0.85);
    }

    public void OnMoveSent()
    {
        _lastMoveSent = DateTime.UtcNow;
    }
}
