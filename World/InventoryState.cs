namespace StressBotBenchmark.World;

/// <summary>
/// Tracks equipment slots and open containers for potion/item usage.
/// </summary>
public sealed class InventoryState
{
    /// <summary>Equipment slots (1-10 = head, necklace, backpack, armor, right, left, legs, feet, ring, ammo).</summary>
    private readonly ushort[] _slots = new ushort[11]; // index 0 unused, 1..10

    /// <summary>Open containers: cid → list of (clientId, count).</summary>
    private readonly Dictionary<byte, List<(ushort ClientId, byte Count)>> _containers = new();

    public void SetSlot(byte slot, ushort clientId)
    {
        if (slot >= 1 && slot <= 10)
            _slots[slot] = clientId;
    }

    public void ClearSlot(byte slot)
    {
        if (slot >= 1 && slot <= 10)
            _slots[slot] = 0;
    }

    public ushort GetSlot(byte slot) =>
        slot >= 1 && slot <= 10 ? _slots[slot] : (ushort)0;

    public void OpenContainer(byte cid, List<(ushort ClientId, byte Count)> items)
    {
        _containers[cid] = items;
    }

    public void CloseContainer(byte cid)
    {
        _containers.Remove(cid);
    }

    public void AddContainerItem(byte cid, ushort clientId, byte count)
    {
        if (_containers.TryGetValue(cid, out var items))
            items.Insert(0, (clientId, count));
    }

    public void UpdateContainerItem(byte cid, ushort slot, ushort clientId, byte count)
    {
        if (_containers.TryGetValue(cid, out var items) && slot < items.Count)
            items[slot] = (clientId, count);
    }

    public void RemoveContainerItem(byte cid, ushort slot)
    {
        if (_containers.TryGetValue(cid, out var items) && slot < items.Count)
            items.RemoveAt(slot);
    }

    /// <summary>Find a consumable by client ID in any open container. Returns (cid, slot) or null.</summary>
    public (byte Cid, ushort Slot)? FindItem(ushort clientId)
    {
        foreach (var (cid, items) in _containers)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ClientId == clientId)
                    return (cid, (ushort)i);
            }
        }
        return null;
    }

    /// <summary>Count total of a client ID across all open containers.</summary>
    public int CountItem(ushort clientId)
    {
        int total = 0;
        foreach (var (_, items) in _containers)
        {
            foreach (var (id, count) in items)
            {
                if (id == clientId)
                    total += count > 0 ? count : 1;
            }
        }
        return total;
    }

    public void Clear()
    {
        Array.Clear(_slots);
        _containers.Clear();
    }
}
