namespace StressBotBenchmark.World;

/// <summary>
/// Per-bot local world state aggregating player, creatures, tiles, and inventory.
/// Updated exclusively by the protocol parser from received packets.
/// </summary>
public sealed class WorldState
{
    public PlayerState Player { get; } = new();
    public InventoryState Inventory { get; } = new();

    /// <summary>All known creatures by ID.</summary>
    private readonly Dictionary<uint, CreatureState> _creatures = new();

    /// <summary>Tile cache keyed by packed position.</summary>
    private readonly Dictionary<long, TileState> _tiles = new();

    /// <summary>Known creature set — mirrors the TFS server-side known list (max ~250).</summary>
    private readonly HashSet<uint> _knownCreatureIds = new();

    // ── Creatures ───────────────────────────────────────────

    public IReadOnlyDictionary<uint, CreatureState> Creatures => _creatures;

    public CreatureState? GetCreature(uint id) =>
        _creatures.TryGetValue(id, out var c) ? c : null;

    public CreatureState GetOrCreateCreature(uint id)
    {
        if (!_creatures.TryGetValue(id, out var c))
        {
            c = new CreatureState
            {
                Id = id,
                Type = CreatureState.ClassifyById(id)
            };
            _creatures[id] = c;
        }
        return c;
    }

    public void RemoveCreature(uint id)
    {
        if (_creatures.TryGetValue(id, out var c))
        {
            c.Visible = false;
        }
        // Don't remove from dict — keep for stale reference tracking
    }

    /// <summary>Mark a creature as known (protocol-level).</summary>
    public bool IsKnownCreature(uint id) => _knownCreatureIds.Contains(id);

    public void MarkCreatureKnown(uint id) => _knownCreatureIds.Add(id);

    /// <summary>Get all visible monsters on the same floor.</summary>
    public IEnumerable<CreatureState> GetVisibleMonsters()
    {
        foreach (var c in _creatures.Values)
        {
            if (c.Visible && c.Type == CreatureType.Monster &&
                c.HealthPercent > 0 && c.Z == Player.Z)
            {
                yield return c;
            }
        }
    }

    /// <summary>Count visible monsters.</summary>
    public int CountVisibleMonsters()
    {
        int count = 0;
        foreach (var c in _creatures.Values)
        {
            if (c.Visible && c.Type == CreatureType.Monster && c.HealthPercent > 0)
                count++;
        }
        return count;
    }

    // ── Tiles ───────────────────────────────────────────────

    private static long PackPosition(ushort x, ushort y, byte z) =>
        ((long)z << 32) | ((long)y << 16) | x;

    public TileState? GetTile(ushort x, ushort y, byte z) =>
        _tiles.TryGetValue(PackPosition(x, y, z), out var t) ? t : null;

    public TileState GetOrCreateTile(ushort x, ushort y, byte z)
    {
        long key = PackPosition(x, y, z);
        if (!_tiles.TryGetValue(key, out var t))
        {
            t = new TileState(x, y, z);
            _tiles[key] = t;
        }
        return t;
    }

    /// <summary>Move a creature between tiles.</summary>
    public void MoveCreatureOnMap(uint creatureId, ushort oldX, ushort oldY, byte oldZ,
                                  ushort newX, ushort newY, byte newZ)
    {
        var oldTile = GetTile(oldX, oldY, oldZ);
        oldTile?.RemoveCreature(creatureId);

        var newTile = GetOrCreateTile(newX, newY, newZ);
        newTile.AddCreature(creatureId);

        if (_creatures.TryGetValue(creatureId, out var c))
        {
            c.UpdatePosition(newX, newY, newZ);
        }
    }

    // ── Lifecycle ───────────────────────────────────────────

    /// <summary>Clear all state on disconnect/reconnect.</summary>
    public void Clear()
    {
        _creatures.Clear();
        _tiles.Clear();
        _knownCreatureIds.Clear();
        Inventory.Clear();
        // PlayerState is intentionally not cleared — ID persists across sessions
    }

    /// <summary>Clear map data for a full map refresh (e.g. after teleport/floor change).</summary>
    public void ClearMap()
    {
        _tiles.Clear();
        // Mark all creatures as not visible — the new map will re-add them
        foreach (var c in _creatures.Values)
            c.Visible = false;
        _knownCreatureIds.Clear();
    }

    /// <summary>Prune creatures not seen recently to prevent unbounded growth.</summary>
    public void PruneStaleCreatures(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var toRemove = new List<uint>();
        foreach (var (id, c) in _creatures)
        {
            if (!c.Visible && c.LastSeen < cutoff && id != Player.Id)
                toRemove.Add(id);
        }
        foreach (var id in toRemove)
            _creatures.Remove(id);
    }
}
