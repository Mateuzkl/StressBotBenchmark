namespace StressBotBenchmark.Data;

/// <summary>
/// Lightweight metadata for client item IDs.
/// Determines walkability, blocking, and interactability without full items.otb/dat.
/// </summary>
public sealed class ItemMetadata
{
    // Known blocking items in Tibia 8.60 (walls, statues, large rocks, solid furniture)
    private static readonly HashSet<ushort> _knownBlockingItems = new()
    {
        // Common walls
        1025, 1026, 1027, 1028, 1029, 1030, 1031, 1032, 1033, 1034,
        1035, 1036, 1037, 1038, 1039, 1040, 1041, 1042, 1043, 1044,
        // Stone walls, town walls, railings
        1045, 1046, 1047, 1048, 1049, 1050, 1051, 1052, 1053, 1054,
        1531, 1532, 1533, 1534, 1535, 1536,
        // Statues, pillars, columns
        1386, 1387, 1406, 1407, 1408, 1409, 1410,
        // Solid furniture / counters / dep box
        2589, 2590, 2591, 2592, 3497, 3498, 3499, 3500
    };

    // Known interactive items (stairs, ladders, sewer grates, doors)
    private static readonly HashSet<ushort> _knownStairsOrLadders = new()
    {
        1385, 1386, 1388, 1390, 1392, 1394, // sewer grate, ladders
        411, 412, 413, 414, 432, 433, 434, 437, 438, // wooden/stone stairs
        1947, 1948 // holes / ropespots
    };

    public static bool IsBlocking(ushort clientId)
    {
        return _knownBlockingItems.Contains(clientId);
    }

    public static bool IsStairsOrLadder(ushort clientId)
    {
        return _knownStairsOrLadders.Contains(clientId);
    }
}
