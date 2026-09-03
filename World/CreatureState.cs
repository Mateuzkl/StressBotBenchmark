namespace StressBotBenchmark.World;

/// <summary>
/// Type classification based on TFS creature ID ranges.
/// </summary>
public enum CreatureType : byte
{
    Unknown = 0,
    Player = 1,   // 0x10000000+
    Monster = 2,  // 0x40000000+
    Npc = 3       // 0x80000000+
}

/// <summary>
/// Tracks state of a visible creature, updated from protocol packets.
/// </summary>
public sealed class CreatureState
{
    public uint Id { get; set; }
    public CreatureType Type { get; set; }
    public string Name { get; set; } = "";

    public ushort X { get; set; }
    public ushort Y { get; set; }
    public byte Z { get; set; }

    public byte HealthPercent { get; set; }
    public byte Direction { get; set; }
    public ushort Speed { get; set; }
    public bool Walkthrough { get; set; }

    // Outfit
    public ushort LookType { get; set; }
    public byte LookHead { get; set; }
    public byte LookBody { get; set; }
    public byte LookLegs { get; set; }
    public byte LookFeet { get; set; }
    public byte LookAddons { get; set; }
    public ushort LookMount { get; set; }
    public ushort LookTypeEx { get; set; } // item disguise

    // Tracking
    public bool Visible { get; set; } = true;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastMove { get; set; } = DateTime.MinValue;

    // Skull / party
    public byte Skull { get; set; }
    public byte PartyShield { get; set; }
    public byte GuildEmblem { get; set; }

    /// <summary>Classify a creature by its TFS ID range.</summary>
    public static CreatureType ClassifyById(uint id)
    {
        if (id >= 0x80000000) return CreatureType.Npc;
        if (id >= 0x40000000) return CreatureType.Monster;
        if (id >= 0x10000000) return CreatureType.Player;
        return CreatureType.Unknown;
    }

    public void UpdatePosition(ushort x, ushort y, byte z)
    {
        X = x;
        Y = y;
        Z = z;
        LastMove = DateTime.UtcNow;
        LastSeen = DateTime.UtcNow;
        Visible = true;
    }

    /// <summary>Manhattan distance to a position.</summary>
    public int DistanceTo(ushort tx, ushort ty) =>
        Math.Abs(X - tx) + Math.Abs(Y - ty);

    /// <summary>Chebyshev distance to a position.</summary>
    public int ChebyshevDistanceTo(ushort tx, ushort ty) =>
        Math.Max(Math.Abs(X - tx), Math.Abs(Y - ty));
}
