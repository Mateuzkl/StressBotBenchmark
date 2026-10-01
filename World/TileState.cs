namespace StressBotBenchmark.World;

public readonly record struct TileThing(ushort ItemId, uint CreatureId);

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
    public List<TileThing> Things { get; } = new(10);

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
        Things.Clear();
    }

    public void AddItem(ushort itemId, int? stackpos = null)
    {
        int index = stackpos ?? Things.Count;
        Things.Insert(Math.Clamp(index, 0, Things.Count), new TileThing(itemId, 0));
        SyncItems();
    }

    private void SyncItems()
    {
        GroundId = 0;
        ItemIds.Clear();
        foreach (var thing in Things)
        {
            if (thing.ItemId == 0) continue;
            bool ground = Data.ItemCatalog.Current?.Get(thing.ItemId).Ground ?? GroundId == 0;
            if (ground) GroundId = thing.ItemId;
            else ItemIds.Add(thing.ItemId);
        }
    }

    public uint RemoveThing(int stackpos)
    {
        if (stackpos >= Things.Count) return 0;
        var thing = Things[stackpos];
        Things.RemoveAt(stackpos);
        if (thing.CreatureId != 0) CreatureIds.Remove(thing.CreatureId);
        SyncItems();
        return thing.CreatureId;
    }

    public void AddCreature(uint creatureId, int? stackpos = null)
    {
        if (!CreatureIds.Contains(creatureId))
        {
            CreatureIds.Add(creatureId);
            int index = stackpos ?? Things.Count;
            Things.Insert(Math.Clamp(index, 0, Things.Count), new TileThing(0, creatureId));
        }
    }

    public void RemoveCreature(uint creatureId)
    {
        CreatureIds.Remove(creatureId);
        Things.RemoveAll(t => t.CreatureId == creatureId);
    }
}
