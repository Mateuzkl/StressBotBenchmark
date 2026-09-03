using StressBotBenchmark.Network;
using StressBotBenchmark.World;

namespace StressBotBenchmark.Protocol;

/// <summary>
/// Parses creature data from server messages exactly as TFS 8.60
/// writes it via AddCreature(). Handles both known (0x62) and unknown (0x61) formats.
/// </summary>
public static class CreatureParser
{
    /// <summary>
    /// Parse a creature from a tile or 0x6A/0x6B packet.
    /// The u16 marker (0x61 or 0x62) has already been read.
    /// Updates the WorldState with the creature data.
    /// </summary>
    public static uint ParseCreature(InputMessage msg, ushort marker, WorldState world)
    {
        uint creatureId;
        CreatureState creature;

        if (marker == ServerOpcodes.UnknownCreature) // 0x61
        {
            uint removeId = msg.GetU32();
            creatureId = msg.GetU32();
            string name = msg.GetString();

            creature = world.GetOrCreateCreature(creatureId);
            creature.Name = name;
            creature.Type = CreatureState.ClassifyById(creatureId);
            creature.Visible = true;
            creature.LastSeen = DateTime.UtcNow;

            world.MarkCreatureKnown(creatureId);
        }
        else if (marker == ServerOpcodes.KnownCreature) // 0x62
        {
            creatureId = msg.GetU32();
            creature = world.GetOrCreateCreature(creatureId);
            creature.Visible = true;
            creature.LastSeen = DateTime.UtcNow;
        }
        else
        {
            // 0x63 = outdated creature (skip u32 id)
            creatureId = msg.GetU32();
            creature = world.GetOrCreateCreature(creatureId);
            creature.Visible = true;
            creature.LastSeen = DateTime.UtcNow;
        }

        // Health percent
        creature.HealthPercent = msg.GetU8();

        // Direction (clamped to 0-3)
        byte direction = msg.GetU8();
        if (direction > 3) direction = 2; // DIRECTION_SOUTH
        creature.Direction = direction;

        // Outfit
        ReadOutfit(msg, creature);

        // Light
        msg.GetU8(); // light level
        msg.GetU8(); // light color

        // Speed
        creature.Speed = msg.GetU16();

        // Skull
        creature.Skull = msg.GetU8();

        // Party shield
        creature.PartyShield = msg.GetU8();

        // Guild emblem (only for unknown creatures)
        if (marker == ServerOpcodes.UnknownCreature)
        {
            creature.GuildEmblem = msg.GetU8();
        }

        // Walkthrough
        creature.Walkthrough = msg.GetU8() == 0x00;

        return creatureId;
    }

    /// <summary>
    /// Read outfit data matching TFS AddOutfit():
    /// u16 lookType + (if type!=0: head/body/legs/feet/addons) + (if type==0: u16 lookTypeEx) + u16 mount
    /// </summary>
    public static void ReadOutfit(InputMessage msg, CreatureState creature)
    {
        ushort lookType = msg.GetU16();
        creature.LookType = lookType;

        if (lookType != 0)
        {
            creature.LookHead = msg.GetU8();
            creature.LookBody = msg.GetU8();
            creature.LookLegs = msg.GetU8();
            creature.LookFeet = msg.GetU8();
            creature.LookAddons = msg.GetU8();
        }
        else
        {
            creature.LookTypeEx = msg.GetU16(); // item disguise
        }

        // Mount (always present for non-v861 clients)
        creature.LookMount = msg.GetU16();
    }
}
