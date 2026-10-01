using StressBotBenchmark.Data;
using StressBotBenchmark.Network;
using StressBotBenchmark.World;

namespace StressBotBenchmark.Protocol;

public static class MapParser
{
    public static void ParseMapDescription(InputMessage msg, WorldState world, ushort x, ushort y, byte z)
        => ParseMapSlice(msg, world, x - 8, y - 6, z, 18, 14);

    // GetMapDescription carries its skip count across all visible floors, also
    // for movement strips. A terminator's low byte skips SUBSEQUENT tiles.
    public static void ParseMapSlice(InputMessage msg, WorldState world,
        int startX, int startY, byte z, int width, int height)
    {
        int start = z > 7 ? z - 2 : 7;
        int end = z > 7 ? Math.Min(15, z + 2) : 0;
        int step = z > 7 ? 1 : -1;
        int skip = 0;
        for (int floor = start; floor != end + step; floor += step)
            ParseFloorDescription(msg, world, startX, startY, (byte)floor, width, height, z - floor, ref skip);
        if (skip != 0) throw new InvalidDataException("Map skip extends beyond the advertised viewport.");
    }

    public static void ParseFloorDescription(InputMessage msg, WorldState world,
        int startX, int startY, byte z, int width, int height, int offset, ref int skip)
    {
        for (int nx = 0; nx < width; nx++)
            for (int ny = 0; ny < height; ny++)
            {
                ushort x = (ushort)(startX + nx + offset), y = (ushort)(startY + ny + offset);
                if (skip > 0)
                {
                    skip--;
                    var emptyTile = world.GetTile(x, y, z);
                    if (emptyTile != null)
                    {
                        foreach (uint id in emptyTile.CreatureIds) world.RemoveCreature(id);
                        emptyTile.Clear();
                    }
                }
                else skip = ParseTileDescription(msg, world, x, y, z);
            }
    }

    public static int ParseTileDescription(InputMessage msg, WorldState world, ushort x, ushort y, byte z)
    {
        TileState? tile = world.GetTile(x, y, z);
        if (tile != null)
        {
            foreach (uint id in tile.CreatureIds) world.RemoveCreature(id);
            tile.Clear();
        }
        int count = 0;
        while (true)
        {
            ushort peek = msg.PeekU16(); // Truncation throws; never continue at an unknown boundary.
            if (peek >= 0xFF00) return msg.GetU16() & 0xFF;
            if (count++ >= 10) throw new InvalidDataException("Tile exceeds the TFS 8.60 stack limit.");
            tile ??= world.GetOrCreateTile(x, y, z);
            if (peek is 0x61 or 0x62 or 0x63)
            {
                msg.GetU16();
                uint id = CreatureParser.ParseCreature(msg, peek, world);
                tile.AddCreature(id);
                world.GetCreature(id)?.UpdatePosition(x, y, z);
                if (id == world.Player.Id) world.Player.UpdatePosition(x, y, z);
            }
            else
            {
                ushort id = msg.GetU16();
                if (IsKnownStackableOrFluid(id)) msg.GetU8();
                tile.AddItem(id);
            }
        }
    }

    // Explicitly labelled fallback for deployments without local item metadata.
    // Use itemsOtbPath for validated performance runs and custom client IDs.
    private static readonly HashSet<ushort> CommonExtraByteItems = new()
    {
        3031, 3035, 3043, 3147, 3148, 3149, 3150, 3151, 3152, 3153, 3154,
        3155, 3156, 3157, 3158, 3159, 3160, 3161, 3162, 3163, 3164, 3165,
        3166, 3167, 3168, 3169, 3170, 3171, 3172, 3173, 3174, 3175, 3176,
        7588, 7589, 7590, 7591, 7618, 7620, 8472, 8473, 3447, 3448, 3449,
        3450, 3582, 3577, 3578, 3585, 3586, 3592, 2016, 2019, 2023, 2024, 2025, 2026
    };
    public static bool IsKnownStackableOrFluid(ushort id) =>
        ItemCatalog.Current?.Get(id).ExtraByte ?? CommonExtraByteItems.Contains(id);
}
