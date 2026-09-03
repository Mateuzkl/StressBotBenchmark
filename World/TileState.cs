namespace StressBotBenchmark.World;

/// <summary>
/// Lightweight representation of a map tile. Stores item client IDs and creature IDs
/// for walkability inference and navigation.
/// </summary>
public sealed class TileState
{
    public ushort X { get; }
    public ushort Y { get; }
    public byte Z { get; }

    /// <summary>Ground item client ID, 0 if none.</summary>
    public ushort GroundId { get; set; }

    /// <summary>Top items on this tile (max ~10).</summary>
    public List<ushort> ItemIds { get; } = new(4);

    /// <summary>Creature IDs standing on this tile.</summary>
    public List<uint> CreatureIds { get; } = new(2);

    public TileState(ushort x, ushort y, byte z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// A tile is considered walkable if it has ground and no blocking items.
    /// This is a heuristic — full walkability requires item metadata.
    /// </summary>
    public bool HasGround => GroundId != 0;

    /// <summary>Whether any creature is on this tile.</summary>
    public bool HasCreature => CreatureIds.Count > 0;

    public void Clear()
    {
        GroundId = 0;
        ItemIds.Clear();
        CreatureIds.Clear();
    }

    public void AddCreature(uint creatureId)
    {
        if (!CreatureIds.Contains(creatureId))
            CreatureIds.Add(creatureId);
    }

    public void RemoveCreature(uint creatureId)
    {
        CreatureIds.Remove(creatureId);
    }
}
