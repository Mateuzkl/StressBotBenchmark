namespace StressBotBenchmark.World;

/// <summary>
/// Tracks the bot's own player state, updated from protocol packets.
/// </summary>
public sealed class PlayerState
{
    public uint Id { get; set; }
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public byte Z { get; set; }

    public uint Hp { get; set; }
    public uint MaxHp { get; set; }
    public uint Mana { get; set; }
    public uint MaxMana { get; set; }
    public ushort Level { get; set; }

    public byte Direction { get; set; }
    public ushort Speed { get; set; }
    public byte Soul { get; set; }
    public ushort Stamina { get; set; }

    // Outfit
    public ushort LookType { get; set; }
    public byte LookHead { get; set; }
    public byte LookBody { get; set; }
    public byte LookLegs { get; set; }
    public byte LookFeet { get; set; }
    public byte LookAddons { get; set; }
    public ushort LookMount { get; set; }

    // Combat state
    public uint CurrentTargetId { get; set; }
    public bool Mounted => LookMount != 0;

    // Damage tracking
    public DateTime LastDamageTakenTime { get; set; } = DateTime.MinValue;
    public DateTime LastMoveTime { get; set; } = DateTime.MinValue;
    public DateTime LastCombatTime { get; set; } = DateTime.MinValue;
    public uint PreviousHp { get; set; }

    // Derived
    public double HpPercent => MaxHp > 0 ? (double)Hp / MaxHp * 100.0 : 100.0;
    public double ManaPercent => MaxMana > 0 ? (double)Mana / MaxMana * 100.0 : 100.0;

    public void UpdatePosition(ushort x, ushort y, byte z)
    {
        X = x;
        Y = y;
        Z = z;
        LastMoveTime = DateTime.UtcNow;
    }

    /// <summary>Update stats from a 0xA0 packet. Detects damage.</summary>
    public void UpdateStats(uint hp, uint maxHp, uint mana, uint maxMana, ushort level)
    {
        PreviousHp = Hp;
        Hp = hp;
        MaxHp = maxHp;
        Mana = mana;
        MaxMana = maxMana;
        Level = level;

        if (hp < PreviousHp && PreviousHp > 0)
        {
            LastDamageTakenTime = DateTime.UtcNow;
        }
    }
}
