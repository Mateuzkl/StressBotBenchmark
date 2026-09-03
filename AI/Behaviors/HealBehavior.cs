using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;

namespace StressBotBenchmark.AI.Behaviors;

/// <summary>
/// Handles real player healing based on PlayerState HP/Mana percentages.
/// Evaluates Heal2 (strong heal) first, then Heal1 (light heal), then HealMana.
/// </summary>
public sealed class HealBehavior
{
    public OutputMessage? Evaluate(DecisionContext ctx)
    {
        var player = ctx.World.Player;
        if (player.MaxHp == 0) return null;
        if (!ctx.Cooldowns.IsReady("healing_group")) return null;

        var voc = ctx.Config.VocationConfig;
        double hpPercent = player.HpPercent;
        double manaPercent = player.ManaPercent;

        // 1. Strong Heal (Heal2)
        if (voc.Heal2.Enabled && !string.IsNullOrWhiteSpace(voc.Heal2.SpellText))
        {
            if (hpPercent <= voc.Heal2.ThresholdPercent && ctx.Cooldowns.IsReady("heal2"))
            {
                ctx.Cooldowns.SetCooldown("heal2", voc.Heal2.CooldownMs);
                ctx.Cooldowns.SetCooldown("healing_group", 1000); // 1s TFS healing group cooldown
                ctx.Cooldowns.TriggerGlobalActionCooldown(1000);
                return Protocol860Writer.Say(voc.Heal2.SpellText, 1);
            }
        }

        // 2. Light Heal (Heal1)
        if (voc.Heal1.Enabled && !string.IsNullOrWhiteSpace(voc.Heal1.SpellText))
        {
            if (hpPercent <= voc.Heal1.ThresholdPercent && ctx.Cooldowns.IsReady("heal1"))
            {
                ctx.Cooldowns.SetCooldown("heal1", voc.Heal1.CooldownMs);
                ctx.Cooldowns.SetCooldown("healing_group", 1000); // 1s TFS healing group cooldown
                ctx.Cooldowns.TriggerGlobalActionCooldown(1000);
                return Protocol860Writer.Say(voc.Heal1.SpellText, 1);
            }
        }

        // 3. Mana Restore Spell (HealMana)
        if (voc.HealMana.Enabled && !string.IsNullOrWhiteSpace(voc.HealMana.SpellText))
        {
            if (manaPercent <= voc.HealMana.ThresholdPercent && ctx.Cooldowns.IsReady("heal_mana"))
            {
                ctx.Cooldowns.SetCooldown("heal_mana", voc.HealMana.CooldownMs);
                ctx.Cooldowns.TriggerGlobalActionCooldown(1000);
                return Protocol860Writer.Say(voc.HealMana.SpellText, 1);
            }
        }

        return null;
    }
}
