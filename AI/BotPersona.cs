namespace StressBotBenchmark.AI;

/// <summary>
/// Per-bot personality and combat attributes.
/// Deterministically generated using a seed so benchmarks are reproducible.
/// </summary>
public sealed class BotPersona
{
    public Vocation Vocation { get; set; }
    public int PreferredRange { get; set; } = 1; // 1 for Knight, 3-4 for Paladin, 4-5 for Mage
    public double Aggression { get; set; } = 0.8;
    public double RiskTolerance { get; set; } = 0.5;
    public double ExploreChance { get; set; } = 0.5;
    public double ChatChance { get; set; } = 0.005; // 0.5% per tick max (very rare)
    public double OutfitChance { get; set; } = 0.0005; // Rare cosmetic change
    public double MountChance { get; set; } = 0.0005;
    public int Seed { get; }
    public Random Rng { get; }

    public BotPersona(int seed, VocationProfile? vocationProfile = null)
    {
        Seed = seed;
        Rng = new Random(seed);

        if (vocationProfile != null)
        {
            Vocation = vocationProfile.Vocation;
        }
        else
        {
            // Random vocation if none specified
            Vocation = (Vocation)Rng.Next(0, 4);
        }

        switch (Vocation)
        {
            case Vocation.Knight:
                PreferredRange = 1; // Melee
                Aggression = 0.9;
                RiskTolerance = 0.7;
                break;
            case Vocation.Paladin:
                PreferredRange = 3 + Rng.Next(0, 2); // 3-4
                Aggression = 0.7;
                RiskTolerance = 0.5;
                break;
            case Vocation.Sorcerer:
            case Vocation.Druid:
                PreferredRange = 4 + Rng.Next(0, 2); // 4-5
                Aggression = 0.6;
                RiskTolerance = 0.3; // Mages are squishy, flee earlier
                break;
        }
    }
}
