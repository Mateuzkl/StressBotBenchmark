using StressBotBenchmark.Network;
using StressBotBenchmark.World;

namespace StressBotBenchmark.Protocol;

/// <summary>
/// Telemetry for unknown/failed opcodes.
/// </summary>
public sealed class OpcodeStats
{
    public int Count;
    public DateTime FirstSeen = DateTime.UtcNow;
    public int LastPayloadRemaining;
}

/// <summary>
/// Structured parser for TFS 8.60 protocol (non-OTC, OS 2).
/// Replaces the old opcode switch + TrackLegacyCombat approach.
///
/// Design:
/// - Processes all opcodes whose layout is confirmed from TFS protocolgame.cpp
/// - Unknown opcodes are counted in telemetry but STOP parsing that payload
///   (we cannot skip an unknown opcode without knowing its size)
/// - Never scans arbitrary bytes — all reads are structured
/// </summary>
public sealed class Protocol860Parser
{
    private readonly WorldState _world;
    private readonly BotMetrics _metrics;

    // Callbacks to TibiaBot for events that require network responses
    public Action? OnPingReceived;
    public Action<string>? OnDisconnect;
    public Action<string, byte>? OnDisconnectWait;
    public Action? OnLoginAck;
    public Action<string>? OnTextMessage;

    // Outfit window data (stored for AI to use)
    public List<(ushort LookType, string Name, byte Addons)>? AvailableOutfits { get; private set; }
    public List<(ushort MountId, string Name)>? AvailableMounts { get; private set; }

    // Unknown opcode telemetry
    private readonly Dictionary<byte, OpcodeStats> _unknownOpcodes = new();
    public IReadOnlyDictionary<byte, OpcodeStats> UnknownOpcodes => _unknownOpcodes;
    private int _totalParserErrors;
    public int ParserErrors => _totalParserErrors;

    public Protocol860Parser(WorldState world, BotMetrics metrics)
    {
        _world = world;
        _metrics = metrics;
    }

    /// <summary>
    /// Process a decrypted payload. May contain multiple opcodes.
    /// Returns true if the bot is now in-world (login ack received).
    /// </summary>
    public bool ProcessPayload(InputMessage msg, bool inWorld)
    {
        bool becameInWorld = false;

        while (msg.Remaining > 0)
        {
            int posBeforeOpcode = msg.Position;
            byte opcode = msg.GetU8();

            try
            {
                switch (opcode)
                {
                    // ── Login / Disconnect ──────────────────────
                    case ServerOpcodes.LoginAck: // 0x0A
                        ParseLoginAck(msg);
                        becameInWorld = true;
                        continue;

                    case ServerOpcodes.Disconnect: // 0x14
                        OnDisconnect?.Invoke(msg.GetString());
                        return becameInWorld;

                    case ServerOpcodes.DisconnectWait: // 0x16
                        string reason = msg.GetString();
                        byte retrySeconds = msg.GetU8();
                        OnDisconnectWait?.Invoke(reason, retrySeconds);
                        return becameInWorld;

                    // ── Ping ────────────────────────────────────
                    case ServerOpcodes.Ping:     // 0x1E
                    case ServerOpcodes.PingBack:  // 0x1D
                        OnPingReceived?.Invoke();
                        continue;

                    // ── Map ─────────────────────────────────────
                    case ServerOpcodes.MapDescription: // 0x64
                        ParseMapDescription(msg);
                        continue;

                    case ServerOpcodes.MapSliceNorth: // 0x65
                    case ServerOpcodes.MapSliceEast:  // 0x66
                    case ServerOpcodes.MapSliceSouth: // 0x67
                    case ServerOpcodes.MapSliceWest:  // 0x68
                        ParseMapSlice(msg, opcode);
                        continue;

                    // ── Tile updates ────────────────────────────
                    case ServerOpcodes.AddTileThing:    // 0x6A
                        ParseAddTileThing(msg);
                        continue;

                    case ServerOpcodes.UpdateTileThing: // 0x6B
                        ParseUpdateTileThing(msg);
                        continue;

                    case ServerOpcodes.RemoveTileThing: // 0x6C
                        ParseRemoveTileThing(msg);
                        continue;

                    case ServerOpcodes.MoveCreature:    // 0x6D
                        ParseMoveCreature(msg);
                        continue;

                    // ── Inventory ───────────────────────────────
                    case ServerOpcodes.InventorySet:   // 0x78
                        ParseInventorySet(msg);
                        continue;

                    case ServerOpcodes.InventoryClear: // 0x79
                        _world.Inventory.ClearSlot(msg.GetU8());
                        continue;

                    // ── Effects (skip) ──────────────────────────
                    case ServerOpcodes.WorldLight: // 0x82
                        msg.Skip(2); // level + color
                        continue;

                    case ServerOpcodes.MagicEffect: // 0x83
                        msg.Skip(5); // position(5) 
                        msg.Skip(2); // u16 type — TFS uses addU16 for effect type
                        continue;

                    case ServerOpcodes.DistanceShoot: // 0x85
                        msg.Skip(5 + 5); // fromPos + toPos
                        msg.Skip(2); // u16 type
                        continue;

                    case ServerOpcodes.CreatureSquare: // 0x86
                        msg.Skip(4 + 1); // creatureId + color
                        continue;

                    // ── Creature updates ────────────────────────
                    case ServerOpcodes.CreatureHealth: // 0x8C
                        ParseCreatureHealth(msg);
                        continue;

                    case ServerOpcodes.CreatureLight: // 0x8D
                        msg.Skip(4 + 1 + 1); // creatureId + level + color
                        continue;

                    case ServerOpcodes.CreatureOutfit: // 0x8E
                        ParseCreatureOutfit(msg);
                        continue;

                    case ServerOpcodes.CreatureSpeed: // 0x8F
                        ParseCreatureSpeed(msg);
                        continue;

                    // ── Player stats / skills ───────────────────
                    case ServerOpcodes.PlayerStats: // 0xA0
                        ParsePlayerStats(msg);
                        continue;

                    case ServerOpcodes.PlayerSkills: // 0xA1
                        msg.Skip(14); // 7 skills × (u8 level + u8 percent) for non-OTC
                        continue;

                    case ServerOpcodes.PlayerIcons: // 0xA2
                        msg.Skip(2); // u16 icons
                        continue;

                    // ── Cancel ──────────────────────────────────
                    case ServerOpcodes.CancelWalk: // 0xB5
                        byte cancelDir = msg.GetU8();
                        _world.Player.Direction = cancelDir;
                        continue;

                    case ServerOpcodes.CancelTarget: // 0xA3
                        _world.Player.CurrentTargetId = 0;
                        // TFS sends u32(0) after cancel target
                        if (msg.Remaining >= 4) msg.Skip(4);
                        continue;

                    // ── Text ────────────────────────────────────
                    case ServerOpcodes.TextMessage: // 0xB4
                        msg.GetU8(); // message class
                        string text = msg.GetString();
                        OnTextMessage?.Invoke(text);
                        continue;

                    // ── Floor changes ───────────────────────────
                    case ServerOpcodes.FloorUp:   // 0xBE
                    case ServerOpcodes.FloorDown: // 0xBF
                        ParseFloorChange(msg, opcode);
                        continue;

                    // ── Outfit window ───────────────────────────
                    case ServerOpcodes.OutfitWindow: // 0xC8
                        ParseOutfitWindow(msg);
                        continue;

                    // ── FYI ─────────────────────────────────────
                    case ServerOpcodes.FYIBox: // 0x15
                        msg.GetString(); // just consume it
                        continue;

                    // ── Container opcodes ───────────────────────
                    case 0x6E: // container open
                        ParseContainerOpen(msg);
                        continue;

                    case 0x6F: // container close
                        _world.Inventory.CloseContainer(msg.GetU8());
                        continue;

                    case 0x70: // container add item
                        ParseContainerAddItem(msg);
                        continue;

                    case 0x71: // container update item
                        ParseContainerUpdateItem(msg);
                        continue;

                    case 0x72: // container remove item
                        ParseContainerRemoveItem(msg);
                        continue;

                    // ── Creature say ────────────────────────────
                    case 0xAA: // creature say
                        ParseCreatureSay(msg);
                        continue;

                    // ── Skull / Shield / Walkthrough ────────────
                    case 0x90: // creature skull
                        ParseCreatureSkull(msg);
                        continue;
                    case 0x91: // creature shield
                        msg.Skip(4 + 1); // creatureId + shield
                        continue;
                    case 0x92: // creature walkthrough
                        ParseCreatureWalkthrough(msg);
                        continue;

                    // ── Unknown ─────────────────────────────────
                    default:
                        RecordUnknownOpcode(opcode, msg.Remaining);
                        // Cannot continue — we don't know the opcode's payload size
                        return becameInWorld;
                }
            }
            catch (InvalidDataException)
            {
                Interlocked.Increment(ref _totalParserErrors);
                // Parsing failed partway through this opcode.
                // We cannot recover position, so abort this payload.
                return becameInWorld;
            }
        }

        return becameInWorld;
    }

    // ── Parse methods ───────────────────────────────────────

    private void ParseLoginAck(InputMessage msg)
    {
        uint playerId = msg.GetU32();
        msg.Skip(2); // beat duration
        msg.Skip(1); // can report bugs
        _world.Player.Id = playerId;
        OnLoginAck?.Invoke();
    }

    private void ParseMapDescription(InputMessage msg)
    {
        var (x, y, z) = msg.GetPosition();
        _world.Player.UpdatePosition(x, y, z);
        MapParser.ParseMapDescription(msg, _world, x, y, z);
    }

    private void ParseMapSlice(InputMessage msg, byte opcode)
    {
        var p = _world.Player;
        int x = p.X;
        int y = p.Y;
        byte z = p.Z;

        // Map slices send 1 row or column of the new area
        switch (opcode)
        {
            case ServerOpcodes.MapSliceNorth: // 0x65 — new north row
                MapParser.ParseMapSlice(msg, _world, x - 8, y - 6, z, 18, 1);
                break;
            case ServerOpcodes.MapSliceEast: // 0x66 — new east column
                MapParser.ParseMapSlice(msg, _world, x + 9, y - 6, z, 1, 14);
                break;
            case ServerOpcodes.MapSliceSouth: // 0x67 — new south row
                MapParser.ParseMapSlice(msg, _world, x - 8, y + 7, z, 18, 1);
                break;
            case ServerOpcodes.MapSliceWest: // 0x68 — new west column
                MapParser.ParseMapSlice(msg, _world, x - 8, y - 6, z, 1, 14);
                break;
        }
    }

    private void ParseAddTileThing(InputMessage msg)
    {
        var (x, y, z) = msg.GetPosition();
        byte stackpos = msg.GetU8();

        if (msg.Remaining < 2) return;
        ushort peek = msg.PeekU16();

        if (peek == ServerOpcodes.UnknownCreature ||
            peek == ServerOpcodes.KnownCreature ||
            peek == 0x0063)
        {
            msg.GetU16(); // consume marker
            uint creatureId = CreatureParser.ParseCreature(msg, peek, _world);
            var tile = _world.GetOrCreateTile(x, y, z);
            tile.AddCreature(creatureId);
            var creature = _world.GetCreature(creatureId);
            creature?.UpdatePosition(x, y, z);

            if (creatureId == _world.Player.Id)
                _world.Player.UpdatePosition(x, y, z);
        }
        else
        {
            // Item
            ushort clientId = msg.GetU16();
            if (MapParser.IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
                msg.GetU8();

            var tile = _world.GetOrCreateTile(x, y, z);
            if (stackpos == 0)
                tile.GroundId = clientId;
            else
                tile.ItemIds.Add(clientId);
        }
    }

    private void ParseUpdateTileThing(InputMessage msg)
    {
        var (x, y, z) = msg.GetPosition();
        byte stackpos = msg.GetU8();

        if (msg.Remaining < 2) return;
        ushort peek = msg.PeekU16();

        if (peek == ServerOpcodes.UnknownCreature ||
            peek == ServerOpcodes.KnownCreature ||
            peek == 0x0063)
        {
            // Creature turn/update
            msg.GetU16();
            CreatureParser.ParseCreature(msg, peek, _world);
        }
        else
        {
            // Item update
            ushort clientId = msg.GetU16();
            if (MapParser.IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
                msg.GetU8();
        }
    }

    private void ParseRemoveTileThing(InputMessage msg)
    {
        var (x, y, z) = msg.GetPosition();
        byte stackpos = msg.GetU8();

        // We can't easily know if the removed thing was a creature or item
        // without maintaining a full per-tile stack. For now, we let creature
        // tracking update via movement/disappear packets.
    }

    private void ParseMoveCreature(InputMessage msg)
    {
        var (oldX, oldY, oldZ) = msg.GetPosition();
        byte oldStackpos = msg.GetU8();
        var (newX, newY, newZ) = msg.GetPosition();

        // Find which creature moved by checking the old tile
        var oldTile = _world.GetTile(oldX, oldY, oldZ);
        uint movedCreatureId = 0;

        if (oldTile != null && oldStackpos < oldTile.CreatureIds.Count + oldTile.ItemIds.Count + 1)
        {
            // Try to identify the creature from the tile's creature list
            // stackpos counting: ground(0), topItems, creatures, bottomItems
            // Without full stack tracking, we try the first creature on the tile
            if (oldTile.CreatureIds.Count > 0)
            {
                // Simple heuristic: if only one creature, it must be it
                if (oldTile.CreatureIds.Count == 1)
                {
                    movedCreatureId = oldTile.CreatureIds[0];
                }
                else
                {
                    // Try to match by stackpos accounting
                    int creatureIndex = oldStackpos - 1 - oldTile.ItemIds.Count;
                    if (oldTile.GroundId != 0) creatureIndex--;
                    if (creatureIndex >= 0 && creatureIndex < oldTile.CreatureIds.Count)
                        movedCreatureId = oldTile.CreatureIds[creatureIndex];
                    else if (oldTile.CreatureIds.Count > 0)
                        movedCreatureId = oldTile.CreatureIds[0]; // fallback
                }
            }
        }

        if (movedCreatureId != 0)
        {
            _world.MoveCreatureOnMap(movedCreatureId, oldX, oldY, oldZ, newX, newY, newZ);

            if (movedCreatureId == _world.Player.Id)
            {
                _world.Player.UpdatePosition(newX, newY, newZ);
            }
        }
    }

    private void ParseCreatureHealth(InputMessage msg)
    {
        uint creatureId = msg.GetU32();
        byte healthPercent = msg.GetU8();

        var creature = _world.GetCreature(creatureId);
        if (creature != null)
        {
            creature.HealthPercent = healthPercent;
            creature.LastSeen = DateTime.UtcNow;

            if (healthPercent == 0)
                creature.Visible = false;
        }
    }

    private void ParseCreatureOutfit(InputMessage msg)
    {
        uint creatureId = msg.GetU32();
        var creature = _world.GetOrCreateCreature(creatureId);
        CreatureParser.ReadOutfit(msg, creature);
    }

    private void ParseCreatureSpeed(InputMessage msg)
    {
        uint creatureId = msg.GetU32();
        ushort speed = msg.GetU16();

        var creature = _world.GetCreature(creatureId);
        if (creature != null)
            creature.Speed = speed;
    }

    private void ParsePlayerStats(InputMessage msg)
    {
        // Layout for non-OTC, non-Astra, OS 2, version 860:
        // u32 hp + u32 maxHp + u32 capacity + u32 experience +
        // u16 level + u8 levelPercent + u32 mana + u32 maxMana +
        // u8 magicLevel + u8 magicPercent + u8 soul + u16 stamina

        uint hp = msg.GetU32();
        uint maxHp = msg.GetU32();
        msg.Skip(4); // capacity
        msg.Skip(4); // experience
        ushort level = msg.GetU16();
        msg.Skip(1); // level percent
        uint mana = msg.GetU32();
        uint maxMana = msg.GetU32();
        msg.Skip(1); // magic level
        msg.Skip(1); // magic percent
        byte soul = msg.GetU8();
        ushort stamina = msg.GetU16();

        _world.Player.UpdateStats(hp, maxHp, mana, maxMana, level);
        _world.Player.Soul = soul;
        _world.Player.Stamina = stamina;
    }

    private void ParseFloorChange(InputMessage msg, byte opcode)
    {
        var p = _world.Player;
        byte oldZ = p.Z;

        if (opcode == ServerOpcodes.FloorUp) // 0xBE
        {
            byte newZ = (byte)(oldZ - 1);
            p.Z = newZ;

            if (newZ == 7) // surfacing
            {
                // Floors 5 down to 0
                for (int i = 5; i >= 0; i--)
                {
                    int offset = 8 - i;
                    MapParser.ParseFloorDescription(msg, _world,
                        p.X - 8, p.Y - 6, (byte)i, 18, 14, offset);
                }
                SkipTrailingSkip(msg);
            }
            else if (newZ > 7) // still underground
            {
                MapParser.ParseFloorDescription(msg, _world,
                    p.X - 8, p.Y - 6, (byte)(oldZ - 3), 18, 14, 3);
                SkipTrailingSkip(msg);
            }

            // West strip + North strip to fix sync
            MapParser.ParseMapSlice(msg, _world, p.X - 8, p.Y - 5, newZ, 1, 14);
            MapParser.ParseMapSlice(msg, _world, p.X - 8, p.Y - 6, newZ, 18, 1);
        }
        else // 0xBF = FloorDown
        {
            byte newZ = (byte)(oldZ + 1);
            p.Z = newZ;

            if (newZ == 8) // going underground
            {
                for (int i = 0; i < 3; i++)
                {
                    MapParser.ParseFloorDescription(msg, _world,
                        p.X - 8, p.Y - 6, (byte)(newZ + i), 18, 14, -i - 1);
                }
                SkipTrailingSkip(msg);
            }
            else if (newZ > 8 && newZ < 14) // deeper underground
            {
                MapParser.ParseFloorDescription(msg, _world,
                    p.X - 8, p.Y - 6, (byte)(newZ + 2), 18, 14, -3);
                SkipTrailingSkip(msg);
            }

            // East strip + South strip to fix sync
            MapParser.ParseMapSlice(msg, _world, p.X + 9, p.Y - 7, newZ, 1, 14);
            MapParser.ParseMapSlice(msg, _world, p.X - 8, p.Y + 7, newZ, 18, 1);
        }
    }

    private void ParseOutfitWindow(InputMessage msg)
    {
        // Current outfit
        ushort currentLookType = msg.GetU16();
        if (currentLookType != 0)
        {
            msg.Skip(5); // head, body, legs, feet, addons
        }
        else
        {
            msg.Skip(2); // lookTypeEx
        }
        msg.GetU16(); // current mount

        // Available outfits
        int outfitCount = msg.GetU8();
        var outfits = new List<(ushort LookType, string Name, byte Addons)>(outfitCount);
        for (int i = 0; i < outfitCount; i++)
        {
            ushort lookType = msg.GetU16();
            string name = msg.GetString();
            byte addons = msg.GetU8();
            outfits.Add((lookType, name, addons));
        }
        AvailableOutfits = outfits;

        // Available mounts
        int mountCount = msg.GetU8();
        var mounts = new List<(ushort MountId, string Name)>(mountCount);
        for (int i = 0; i < mountCount; i++)
        {
            ushort mountId = msg.GetU16();
            string name = msg.GetString();
            mounts.Add((mountId, name));
        }
        AvailableMounts = mounts;
    }

    private void ParseInventorySet(InputMessage msg)
    {
        byte slot = msg.GetU8();
        ushort clientId = msg.GetU16();
        if (MapParser.IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
            msg.GetU8();
        _world.Inventory.SetSlot(slot, clientId);
    }

    private void ParseContainerOpen(InputMessage msg)
    {
        byte cid = msg.GetU8();
        msg.GetU16(); // container itemId
        msg.GetString(); // container name
        msg.GetU8(); // capacity
        msg.GetU8(); // hasParent
        byte itemCount = msg.GetU8();

        var items = new List<(ushort ClientId, byte Count)>(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            ushort clientId = msg.GetU16();
            byte count = 0;
            if (MapParser.IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
                count = msg.GetU8();
            items.Add((clientId, count));
        }
        _world.Inventory.OpenContainer(cid, items);
    }

    private void ParseContainerAddItem(InputMessage msg)
    {
        byte cid = msg.GetU8();
        ushort clientId = msg.GetU16();
        byte count = 0;
        if (MapParser.IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
            count = msg.GetU8();
        _world.Inventory.AddContainerItem(cid, clientId, count);
    }

    private void ParseContainerUpdateItem(InputMessage msg)
    {
        byte cid = msg.GetU8();
        ushort slot = msg.GetU16();
        ushort clientId = msg.GetU16();
        byte count = 0;
        if (MapParser.IsKnownStackableOrFluid(clientId) && msg.Remaining > 0)
            count = msg.GetU8();
        _world.Inventory.UpdateContainerItem(cid, slot, clientId, count);
    }

    private void ParseContainerRemoveItem(InputMessage msg)
    {
        byte cid = msg.GetU8();
        ushort slot = msg.GetU16();
        _world.Inventory.RemoveContainerItem(cid, slot);
    }

    private void ParseCreatureSay(InputMessage msg)
    {
        // u32 unknown/statementId
        msg.GetU32();
        string name = msg.GetString();
        msg.GetU16(); // level
        byte type = msg.GetU8();

        switch (type)
        {
            case 1: // SAY
            case 2: // WHISPER
            case 3: // YELL
                msg.GetPosition(); // position
                break;
            case 5: // CHANNEL_Y
            case 7: // CHANNEL_R1
            case 10: // CHANNEL_R2
            case 13: // CHANNEL_O
                msg.GetU16(); // channelId
                break;
            case 4: // PRIVATE_PN
            case 11: // PRIVATE_RED
                break;
            // Other types have no extra data
        }

        msg.GetString(); // message text
    }

    private void ParseCreatureSkull(InputMessage msg)
    {
        uint creatureId = msg.GetU32();
        byte skull = msg.GetU8();
        var creature = _world.GetCreature(creatureId);
        if (creature != null)
            creature.Skull = skull;
    }

    private void ParseCreatureWalkthrough(InputMessage msg)
    {
        uint creatureId = msg.GetU32();
        bool walkthrough = msg.GetU8() == 0x00;
        var creature = _world.GetCreature(creatureId);
        if (creature != null)
            creature.Walkthrough = walkthrough;
    }

    private static void SkipTrailingSkip(InputMessage msg)
    {
        if (msg.Remaining >= 2)
        {
            ushort peek = msg.PeekU16();
            if ((peek & 0xFF00) == 0xFF00)
                msg.GetU16();
        }
    }

    private void RecordUnknownOpcode(byte opcode, int remaining)
    {
        if (!_unknownOpcodes.TryGetValue(opcode, out var stats))
        {
            stats = new OpcodeStats();
            _unknownOpcodes[opcode] = stats;
        }
        stats.Count++;
        stats.LastPayloadRemaining = remaining;
    }
}
