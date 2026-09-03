using StressBotBenchmark.World;

namespace StressBotBenchmark.Navigation;

/// <summary>
/// Detects changes in player floor (Z coordinate) and notifies navigation/brain.
/// </summary>
public sealed class FloorTransitionDetector
{
    private byte _lastZ;

    public FloorTransitionDetector(byte initialZ)
    {
        _lastZ = initialZ;
    }

    /// <summary>
    /// Checks if Z changed since last check.
    /// </summary>
    public bool CheckTransition(byte currentZ, out byte oldZ, out byte newZ)
    {
        oldZ = _lastZ;
        newZ = currentZ;

        if (currentZ != _lastZ)
        {
            _lastZ = currentZ;
            return true;
        }

        return false;
    }
}
