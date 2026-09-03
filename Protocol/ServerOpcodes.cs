namespace StressBotBenchmark.Protocol;

/// <summary>
/// Server → Client opcodes for TFS 8.60 (non-OTC, OS 2).
/// Verified against protocolgame.cpp.
/// </summary>
public static class ServerOpcodes
{
    // ── Login / Disconnect ──────────────────────────────
    public const byte LoginAck = 0x0A;          // u32 playerId + u16 beatDuration + u8 canReportBugs
    public const byte Disconnect = 0x14;        // string reason
    public const byte DisconnectWait = 0x16;    // string reason + u8 retrySeconds

    // ── Ping ────────────────────────────────────────────
    public const byte Ping = 0x1E;
    public const byte PingBack = 0x1D;

    // ── FYI ─────────────────────────────────────────────
    public const byte FYIBox = 0x15;

    // ── Map / Movement ──────────────────────────────────
    public const byte MapDescription = 0x64;    // position + map data
    public const byte MapSliceNorth = 0x65;
    public const byte MapSliceEast = 0x66;
    public const byte MapSliceSouth = 0x67;
    public const byte MapSliceWest = 0x68;

    // ── Tile updates ────────────────────────────────────
    public const byte AddTileThing = 0x6A;      // position + stackpos + thing
    public const byte UpdateTileThing = 0x6B;   // position + stackpos + thing
    public const byte RemoveTileThing = 0x6C;   // position + stackpos
    public const byte MoveCreature = 0x6D;      // oldPos + oldStackpos + newPos

    // ── Inventory ───────────────────────────────────────
    public const byte InventorySet = 0x78;      // slot + item
    public const byte InventoryClear = 0x79;    // slot

    // ── Container ───────────────────────────────────────
    public const byte ContainerOpen = 0x6E;
    public const byte ContainerClose = 0x6F;
    // Note: 0x6F also used for turn north in client→server! Different direction.
    public const byte ContainerAddItem = 0x70;
    public const byte ContainerUpdateItem = 0x71;
    public const byte ContainerRemoveItem = 0x72;

    // ── Effects ─────────────────────────────────────────
    public const byte WorldLight = 0x82;        // u8 level + u8 color
    public const byte MagicEffect = 0x83;       // position + u16 type
    public const byte DistanceShoot = 0x85;     // fromPos + toPos + u16 type
    public const byte CreatureSquare = 0x86;    // u32 creatureId + u8 color

    // ── Creature updates ────────────────────────────────
    public const byte CreatureHealth = 0x8C;    // u32 creatureId + u8 healthPercent
    public const byte CreatureLight = 0x8D;     // u32 creatureId + u8 level + u8 color
    public const byte CreatureOutfit = 0x8E;    // u32 creatureId + outfit
    public const byte CreatureSpeed = 0x8F;     // u32 creatureId + u16 speed

    // ── Player stats / skills ───────────────────────────
    public const byte PlayerStats = 0xA0;       // hp, maxHp, cap, xp, level, etc.
    public const byte PlayerSkills = 0xA1;      // 7 skills × (u8 level + u8 percent)
    public const byte PlayerIcons = 0xA2;       // u16 icons

    // ── Text ────────────────────────────────────────────
    public const byte TextMessage = 0xB4;       // u8 class + string
    public const byte CreatureSay = 0xAA;       // u32 id + string name + u16 level + u8 type + ...

    // ── Cancel ──────────────────────────────────────────
    public const byte CancelWalk = 0xB5;        // u8 direction
    public const byte CancelTarget = 0xA3;      // (no payload or u32 0)

    // ── Floor changes ───────────────────────────────────
    public const byte FloorUp = 0xBE;           // + floor data
    public const byte FloorDown = 0xBF;         // + floor data

    // ── Outfit window ───────────────────────────────────
    public const byte OutfitWindow = 0xC8;

    // ── Fight modes ─────────────────────────────────────
    public const byte FightModes = 0xA0;        // (same as stats in client→server, different in server→client context)

    // ── Creature markers ────────────────────────────────
    public const byte CreatureWalkthrough = 0x92;
    public const byte CreatureShield = 0x91;
    public const byte CreatureSkull = 0x90;

    // ── Thing identifiers inside map data ───────────────
    public const ushort UnknownCreature = 0x0061;
    public const ushort KnownCreature = 0x0062;
    public const ushort OutdatedCreature = 0x0063;
}
