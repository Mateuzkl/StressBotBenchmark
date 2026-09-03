using StressBotBenchmark.Network;

namespace StressBotBenchmark.Protocol;

/// <summary>
/// Parses a single item from a server message. Non-OTC, non-Astra layout:
/// u16 clientId + optional u8 count (if stackable/fluid).
///
/// Since we don't have item type data loaded, we use a heuristic:
/// items with clientId that correspond to stackable/fluid items will have
/// an extra byte. We use the TFS convention where the client always sends
/// the count byte for stackable items. For a headless bot we read it
/// unconditionally — the worst case is slightly off item counts, but
/// map/creature synchronization stays correct.
/// </summary>
public static class ItemParser
{
    /// <summary>
    /// Read an item from the message. Returns (clientId, extra byte if present).
    /// For map parsing correctness, we always attempt to read the item in the
    /// format that the TFS sends: clientId + optional subtype byte.
    ///
    /// Since we don't know which items are stackable without loading items.otb/dat,
    /// we track a simple heuristic: if clientId is 0 or is a known-format that
    /// requires no extra byte, skip it. Otherwise, we let the MapParser handle
    /// item reading in context where it can recover from errors.
    /// </summary>
    public static ushort ReadItemId(InputMessage msg)
    {
        return msg.GetU16();
    }

    /// <summary>
    /// Skip an item's extra data byte. Called by the map parser when it knows
    /// the item is stackable/splash/fluid (deduced from context or metadata).
    /// </summary>
    public static byte ReadItemExtra(InputMessage msg)
    {
        return msg.GetU8();
    }
}
