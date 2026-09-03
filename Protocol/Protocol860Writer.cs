using StressBotBenchmark.Network;

namespace StressBotBenchmark.Protocol;

/// <summary>
/// Builds properly formatted client → server packets for TFS 8.60.
/// All layouts confirmed against protocolgame.cpp parsePacketOnDispatcher().
/// </summary>
public static class Protocol860Writer
{
    // ── Movement ────────────────────────────────────────

    /// <summary>Single step in a cardinal or diagonal direction (0x65..0x6D).</summary>
    public static OutputMessage MoveStep(byte directionOpcode)
    {
        var msg = new OutputMessage();
        msg.AddU8(directionOpcode);
        return msg;
    }

    /// <summary>North=0x65, East=0x66, South=0x67, West=0x68, NE=0x6A, SE=0x6B, SW=0x6C, NW=0x6D</summary>
    public static byte DirectionToOpcode(int dx, int dy)
    {
        return (dx, dy) switch
        {
            (0, -1) => 0x65,  // North
            (1, 0)  => 0x66,  // East
            (0, 1)  => 0x67,  // South
            (-1, 0) => 0x68,  // West
            (1, -1) => 0x6A,  // Northeast
            (1, 1)  => 0x6B,  // Southeast
            (-1, 1) => 0x6C,  // Southwest
            (-1, -1) => 0x6D, // Northwest
            _ => 0x65         // Default north
        };
    }

    /// <summary>
    /// Autowalk packet (0x64). TFS reads directions in REVERSE order.
    /// Direction encoding: 1=E, 2=NE, 3=N, 4=NW, 5=W, 6=SW, 7=S, 8=SE
    /// </summary>
    public static OutputMessage AutoWalk(IReadOnlyList<(int Dx, int Dy)> path)
    {
        var msg = new OutputMessage();
        msg.AddU8(0x64);
        msg.AddU8((byte)path.Count);

        // TFS reads in reverse (uses getPreviousByte), so we write in forward order
        // and the server reverses. Actually looking at the TFS code:
        //   msg.skipBytes(numdirs); then for i=0..n: getPreviousByte()
        // This means the server reads backwards. So we write the path reversed.
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var (dx, dy) = path[i];
            byte dir = DeltaToAutoWalkDir(dx, dy);
            msg.AddU8(dir);
        }

        return msg;
    }

    /// <summary>Convert dx/dy to TFS autowalk direction byte.</summary>
    public static byte DeltaToAutoWalkDir(int dx, int dy)
    {
        return (dx, dy) switch
        {
            (1, 0)   => 1,  // East
            (1, -1)  => 2,  // Northeast
            (0, -1)  => 3,  // North
            (-1, -1) => 4,  // Northwest
            (-1, 0)  => 5,  // West
            (-1, 1)  => 6,  // Southwest
            (0, 1)   => 7,  // South
            (1, 1)   => 8,  // Southeast
            _ => 7           // Default south
        };
    }

    /// <summary>Stop autowalk (0x69).</summary>
    public static OutputMessage StopAutoWalk()
    {
        var msg = new OutputMessage();
        msg.AddU8(0x69);
        return msg;
    }

    // ── Turns ───────────────────────────────────────────

    /// <summary>Turn in direction: 0=N(0x6F), 1=E(0x70), 2=S(0x71), 3=W(0x72).</summary>
    public static OutputMessage Turn(byte direction)
    {
        var msg = new OutputMessage();
        msg.AddU8((byte)(0x6F + (direction & 3)));
        return msg;
    }

    // ── Combat ──────────────────────────────────────────

    /// <summary>Attack a creature (0xA1). TFS reads: u32 creatureId + 3×u32 (sequence, ignored).</summary>
    public static OutputMessage Attack(uint creatureId)
    {
        var msg = new OutputMessage();
        msg.AddU8(0xA1);
        msg.AddU32(creatureId);
        msg.AddU32(0); // sequence 1
        msg.AddU32(0); // sequence 2
        msg.AddU32(0); // sequence 3
        return msg;
    }

    /// <summary>Follow a creature (0xA2).</summary>
    public static OutputMessage Follow(uint creatureId)
    {
        var msg = new OutputMessage();
        msg.AddU8(0xA2);
        msg.AddU32(creatureId);
        msg.AddU32(0);
        msg.AddU32(0);
        msg.AddU32(0);
        return msg;
    }

    /// <summary>Cancel attack and follow (0xBE).</summary>
    public static OutputMessage CancelAttackFollow()
    {
        var msg = new OutputMessage();
        msg.AddU8(0xBE);
        return msg;
    }

    /// <summary>Set fight modes (0xA0): fightMode + chaseMode + safeFight.</summary>
    public static OutputMessage FightModes(byte fightMode, byte chaseMode, byte safeFight)
    {
        var msg = new OutputMessage();
        msg.AddU8(0xA0);
        msg.AddU8(fightMode);
        msg.AddU8(chaseMode);
        msg.AddU8(safeFight);
        return msg;
    }

    // ── Chat ────────────────────────────────────────────

    /// <summary>Say text (0x96). Type 1 = say (public).</summary>
    public static OutputMessage Say(string text, byte speakType = 1)
    {
        var msg = new OutputMessage();
        msg.AddU8(0x96);
        msg.AddU8(speakType);
        msg.AddString(text);
        return msg;
    }

    // ── Use Item ────────────────────────────────────────

    /// <summary>
    /// Use item (0x82): position + u16 clientId + u8 stackpos + u8 index.
    /// Used for stairs, bueiros, ladders, etc.
    /// </summary>
    public static OutputMessage UseItem(ushort x, ushort y, byte z, ushort clientId, byte stackpos, byte index = 0)
    {
        var msg = new OutputMessage();
        msg.AddU8(0x82);
        msg.AddU16(x);
        msg.AddU16(y);
        msg.AddU8(z);
        msg.AddU16(clientId);
        msg.AddU8(stackpos);
        msg.AddU8(index);
        return msg;
    }

    /// <summary>
    /// Use item with creature (0x84): position + u16 clientId + u8 stackpos + u32 creatureId.
    /// Used for potions (use on self).
    /// </summary>
    public static OutputMessage UseWithCreature(ushort x, ushort y, byte z,
                                                ushort clientId, byte stackpos, uint creatureId)
    {
        var msg = new OutputMessage();
        msg.AddU8(0x84);
        msg.AddU16(x);
        msg.AddU16(y);
        msg.AddU8(z);
        msg.AddU16(clientId);
        msg.AddU8(stackpos);
        msg.AddU32(creatureId);
        return msg;
    }

    // ── Outfit ──────────────────────────────────────────

    /// <summary>Request outfit window (0xD2).</summary>
    public static OutputMessage RequestOutfit()
    {
        var msg = new OutputMessage();
        msg.AddU8(0xD2);
        return msg;
    }

    /// <summary>
    /// Set outfit (0xD3): u16 lookType + head + body + legs + feet + addons + u16 mount.
    /// Confirmed from parseSetOutfit() — non-OTC, non-v861 reads mount as u16.
    /// </summary>
    public static OutputMessage SetOutfit(ushort lookType, byte head, byte body,
                                          byte legs, byte feet, byte addons, ushort mount)
    {
        var msg = new OutputMessage();
        msg.AddU8(0xD3);
        msg.AddU16(lookType);
        msg.AddU8(head);
        msg.AddU8(body);
        msg.AddU8(legs);
        msg.AddU8(feet);
        msg.AddU8(addons);
        msg.AddU16(mount);
        return msg;
    }

    // ── Ping / Logout ───────────────────────────────────

    /// <summary>Ping (0x1E).</summary>
    public static OutputMessage Ping()
    {
        var msg = new OutputMessage();
        msg.AddU8(0x1E);
        return msg;
    }

    /// <summary>Logout (0x14).</summary>
    public static OutputMessage Logout()
    {
        var msg = new OutputMessage();
        msg.AddU8(0x14);
        return msg;
    }
}
