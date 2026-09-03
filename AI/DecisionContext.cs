using StressBotBenchmark.World;

namespace StressBotBenchmark.AI;

/// <summary>
/// Snapshot context passed to behaviors during a brain tick.
/// </summary>
public sealed class DecisionContext
{
    public WorldState World { get; }
    public BotPersona Persona { get; }
    public CooldownManager Cooldowns { get; }
    public BotConfig Config { get; }

    public DecisionContext(WorldState world, BotPersona persona, CooldownManager cooldowns, BotConfig config)
    {
        World = world;
        Persona = persona;
        Cooldowns = cooldowns;
        Config = config;
    }
}
