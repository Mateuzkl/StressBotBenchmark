using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;

namespace StressBotBenchmark.AI.Behaviors;

/// <summary>
/// Natural humanized chat behavior.
/// Occurs rarely with long intervals and natural variations.
/// </summary>
public sealed class ChatBehavior
{
    private static readonly string[] DefaultMessages =
    {
        "hi", "hello", "lol", "kk", "kkk", "hmm", "go?", "brb",
        "mana", "heal", "gg", "xd", "nice", "thx", "ty", "lf pt",
        "anyone?", "afk", "back", "gl", "hf", "wb",
        "e ai", "opa", "vlw", "ss", "cya"
    };

    public OutputMessage? Evaluate(DecisionContext ctx)
    {
        if (!ctx.Config.EnableChat)
            return null;

        if (!ctx.Cooldowns.IsReady("chat_cd"))
            return null;

        // Roll chance
        if (ctx.Persona.Rng.NextDouble() > ctx.Persona.ChatChance)
            return null;

        IReadOnlyList<string> pool = (ctx.Config.ChatMessages != null && ctx.Config.ChatMessages.Count > 0)
            ? ctx.Config.ChatMessages
            : DefaultMessages;

        string msg = pool[ctx.Persona.Rng.Next(pool.Count)];

        // Long cooldown between 20s and 60s
        int cdMs = ctx.Persona.Rng.Next(20000, 60000);
        ctx.Cooldowns.SetCooldown("chat_cd", cdMs);

        return Protocol860Writer.Say(msg, 1);
    }
}
