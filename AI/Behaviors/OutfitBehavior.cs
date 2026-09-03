using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;

namespace StressBotBenchmark.AI.Behaviors;

/// <summary>
/// Rare outfit cosmetic behavior.
/// Requests outfit or sets valid outfit if available.
/// </summary>
public sealed class OutfitBehavior
{
    public OutputMessage? Evaluate(DecisionContext ctx)
    {
        if (!ctx.Cooldowns.IsReady("outfit_cd"))
            return null;

        if (ctx.Persona.Rng.NextDouble() > ctx.Persona.OutfitChance)
            return null;

        ctx.Cooldowns.SetCooldown("outfit_cd", ctx.Persona.Rng.Next(60000, 300000)); // 1 to 5 min

        // Randomize colors on current lookType
        var p = ctx.World.Player;
        ushort lookType = p.LookType != 0 ? p.LookType : (ushort)128; // default citizen
        byte head = (byte)ctx.Persona.Rng.Next(0, 132);
        byte body = (byte)ctx.Persona.Rng.Next(0, 132);
        byte legs = (byte)ctx.Persona.Rng.Next(0, 132);
        byte feet = (byte)ctx.Persona.Rng.Next(0, 132);
        byte addons = p.LookAddons;
        ushort mount = p.LookMount;

        return Protocol860Writer.SetOutfit(lookType, head, body, legs, feet, addons, mount);
    }
}
