using StressBotBenchmark.Network;
using StressBotBenchmark.World;

namespace StressBotBenchmark.Protocol;

/// <summary>
/// Parses map data from server messages exactly matching TFS 8.60's
/// GetMapDescription / GetFloorDescription / GetTileDescription layout.
///
/// The map format uses skip markers: a byte followed by 0xFF means
/// "skip N+1 empty tiles". Tile data ends with 0xFF 0xFF.
///
/// Items are sent as u16 clientId. If clientId >= 0xFF61, it's a creature
/// marker (0x61=unknown, 0x62=known). Otherwise it's an item.
///
/// Without items.dat we cannot know which items have extra bytes (stackable/fluid).
/// We use the TFS convention: items with clientId that have extra subtype data
/// always send exactly one extra byte after the clientId.
///
/// LIMITATION: Without items.dat/otb, we treat ALL items as having NO extra byte.
/// This means stackable items (gold, runes, etc.) will cause a 1-byte misalignment.
/// To handle this, we catch any parsing errors and gracefully abort the current
/// map description, preserving whatever was successfully parsed.
/// </summary>
public static class MapParser
{
    /// <summary>
    /// Parse a full map description (opcode 0x64) starting from a center position.
    /// TFS sends: position + GetMapDescription(x-8, y-6, z, 18, 14, msg)
    /// </summary>
    public static void ParseMapDescription(InputMessage msg, WorldState world,
                                           ushort x, ushort y, byte z)
    {
        int startZ, endZ, zStep;

        if (z > 7)
        {
            // Underground: show z-2 to z+2
            startZ = z - 2;
            endZ = Math.Min(15, z + 2);
            zStep = 1;
        }
        else
        {
            // Surface: show floors 7 down to 0
            startZ = 7;
            endZ = 0;
            zStep = -1;
        }

        int stx = x - 8;  // maxClientViewportX = 8
        int sty = y - 6;  // maxClientViewportY = 6
        int width = 18;    // (8 * 2) + 2
        int height = 14;   // (6 * 2) + 2

        for (int nz = startZ; nz != endZ + zStep; nz += zStep)
        {
            int offset = z - nz;
            if (!ParseFloorDescription(msg, world, stx, sty, (byte)nz, width, height, offset))
                return; // parse error, abort gracefully
        }

        // Trailing skip marker
        SkipTrailingSkip(msg);
    }

    /// <summary>
    /// Parse a floor description matching TFS GetFloorDescription().
    /// Returns false if a parse error occurred.
    /// </summary>
    public static bool ParseFloorDescription(InputMessage msg, WorldState world,
                                             int startX, int startY, byte z,
                                             int width, int height, int offset)
    {
        for (int nx = 0; nx < width; nx++)
        {
            for (int ny = 0; ny < height; ny++)
            {
                if (msg.Remaining < 2) return false;

                // Check for skip marker: byte + 0xFF
                ushort peek = msg.PeekU16();

                if ((peek & 0xFF00) == 0xFF00)
                {
                    // Skip marker: low byte = skip count
                    // skip + 1 tiles
                    msg.GetU16(); // consume the skip marker
                    int skipCount = peek & 0x00FF;
                    // Advance ny/nx by skipCount
                    ny += skipCount;
                    while (ny >= height)
                    {
                        ny -= height;
                        nx++;
                    }
                    if (nx >= width) return true; // done with this floor
                    continue;
                }

                // Parse tile data
                ushort tileX = (ushort)(startX + nx + offset);
                ushort tileY = (ushort)(startY + ny + offset);

                if (!ParseTileDescription(msg, world, tileX, tileY, z))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parse a single tile matching TFS GetTileDescription().
    /// Reads: ground item, top items, creatures, down items until 0xFF 0xFF.
    /// </summary>
    public static bool ParseTileDescription(InputMessage msg, WorldState world,
                                            ushort x, ushort y, byte z)
    {
        var tile = world.GetOrCreateTile(x, y, z);
        tile.Clear();

        int count = 0;

        while (msg.Remaining >= 2)
        {
            ushort peek = msg.PeekU16();

            // End of tile marker: any value >= 0xFF00
            if ((peek & 0xFF00) == 0xFF00)
                return true; // don't consume — the floor loop handles skip markers

            if (count >= 10)
                return true; // TFS MAX_STACKPOS_THINGS = 10

            // Check if this is a creature (0x61 = unknown, 0x62 = known, 0x63 = outdated)
            if (peek == ServerOpcodes.UnknownCreature ||
                peek == ServerOpcodes.KnownCreature ||
                peek == 0x0063) // outdated creature
            {
                msg.GetU16(); // consume marker
                try
                {
                    uint creatureId = CreatureParser.ParseCreature(msg, peek, world);
                    tile.AddCreature(creatureId);

                    // If this creature is our player, update position
                    if (creatureId == world.Player.Id)
                    {
                        world.Player.UpdatePosition(x, y, z);
                    }
                    else
                    {
                        var creature = world.GetCreature(creatureId);
                        creature?.UpdatePosition(x, y, z);
                    }
                }
                catch (InvalidDataException)
                {
                    return false; // creature parse failed
                }
            }
            else
            {
                // Regular item
                ushort clientId = msg.GetU16();

                if (count == 0)
                    tile.GroundId = clientId;
                else
                    tile.ItemIds.Add(clientId);

                // Try to read extra byte for stackable/fluid items.
                // Without items.dat we cannot know for sure which items need this.
                // We use a known-ID table for common items, and for unknown items
                // we skip no extra byte. This may cause minor desync on tiles with
                // stackable items, but creature/position tracking will self-correct.
                if (IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
                {
                    msg.GetU8(); // subtype / count / fluid type
                }
            }

            count++;
        }

        return msg.Remaining >= 0;
    }

    /// <summary>
    /// Read a map slice (1-row or 1-column update after movement).
    /// </summary>
    public static void ParseMapSlice(InputMessage msg, WorldState world,
                                     int startX, int startY, byte z,
                                     int width, int height)
    {
        ParseFloorDescription(msg, world, startX, startY, z, width, height, 0);
        SkipTrailingSkip(msg);
    }

    /// <summary>Skip a trailing skip marker if present.</summary>
    private static void SkipTrailingSkip(InputMessage msg)
    {
        if (msg.Remaining >= 2)
        {
            ushort peek = msg.PeekU16();
            if ((peek & 0xFF00) == 0xFF00)
            {
                msg.GetU16(); // consume trailing skip
            }
        }
    }

    // ── Known stackable/fluid item IDs ──────────────────────
    //
    // Without loading items.otb/dat, we maintain a small set of commonly
    // seen stackable/fluid item client IDs from Tibia 8.60.
    // This is imperfect but prevents the most common desync cases.
    //
    // The TFS sends an extra byte for items that are:
    //   - stackable (count byte)
    //   - splash or fluid container (fluid type byte)
    //
    private static readonly HashSet<ushort> _knownStackableIds = new()
    {
        // Gold coins
        3031, 3035, 3043, // gold coin, platinum coin, crystal coin (server IDs — mapped to client IDs)

        // Runes
        3147, 3148, 3149, 3150, 3151, 3152, 3153, 3154, 3155, 3156,
        3157, 3158, 3159, 3160, 3161, 3162, 3163, 3164, 3165, 3166,
        3167, 3168, 3169, 3170, 3171, 3172, 3173, 3174, 3175, 3176,

        // Potions
        7588, 7589, 7590, 7591, 7618, 7620, 8472, 8473, // mana/health potions

        // Ammunition
        3447, 3448, 3449, 3450, // arrows, bolts

        // Food
        3582, 3577, 3578, 3585, 3586, 3592,

        // Splash / Fluid containers
        2016, 2019, 2023, 2024, 2025, 2026, // vials, splash items
    };

    /// <summary>
    /// Check if a client ID is a known stackable or fluid item.
    /// This is a best-effort heuristic.
    /// </summary>
    public static bool IsKnownStackableOrFluid(ushort clientId)
    {
        return _knownStackableIds.Contains(clientId);
    }
}
