using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;
using StressBotBenchmark.World;

namespace StressBotBenchmark.AI.Behaviors;

/// <summary>
/// Evaluates and generates combat actions (attacks, offensive spells).
/// Enforces all strict rules from prompt:
/// - NEVER casts offensive spell without a valid target!
/// - Verifies mana >= MinManaPercent
/// - Verifies target distance is within spell range (e.g. exori = 1 sqm melee)
/// - Respects spell slot cooldown and global exhaust
/// </summary>
public sealed class CombatBehavior
{
    private DateTime _lastAttackPacketSent = DateTime.MinValue;
    private uint _lastAttackedTargetId = 0;

    public OutputMessage? Evaluate(DecisionContext ctx, CreatureState target)
    {
        var player = ctx.World.Player;

        // 1. Attack packet (0xA1): send if target changed or at least every 4s to maintain target
        if (_lastAttackedTargetId != target.Id || (DateTime.UtcNow - _lastAttackPacketSent).TotalSeconds >= 4.0)
        {
            _lastAttackedTargetId = target.Id;
            _lastAttackPacketSent = DateTime.UtcNow;
            return Protocol860Writer.Attack(target.Id);
        }

        // 2. Offensive spells: ONLY IF GLOBAL ACTION COOLDOWN AND ATTACK GROUP COOLDOWN ARE READY
        if (!ctx.Cooldowns.IsActionReady() || !ctx.Cooldowns.IsReady("attack_group"))
            return null;

        var vocConfig = ctx.Config.VocationConfig;
        var slots = new[] { vocConfig.Spell1, vocConfig.Spell2, vocConfig.Spell3, vocConfig.Spell4 };

        int distToTarget = target.ChebyshevDistanceTo(player.X, player.Y);
        double manaPercent = player.ManaPercent;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (!slot.Enabled || string.IsNullOrWhiteSpace(slot.SpellText))
                continue;

            string cdKey = $"spell_slot_{i}";
            if (!ctx.Cooldowns.IsReady(cdKey))
                continue;

            // Mana check
            if (manaPercent < slot.MinManaPercent)
                continue;

            // Range check based on spell
            string spellLower = slot.SpellText.Trim().ToLowerInvariant();
            int maxRange = 4;
            if (spellLower == "exori" || spellLower == "exori gran" || spellLower == "exori mas" || spellLower == "exori hur")
            {
                // Melee / area adjacent spell
                maxRange = (spellLower == "exori hur") ? 4 : 1;
            }

            if (distToTarget > maxRange)
                continue; // Target is too far for this spell!

            // Cast spell!
            ctx.Cooldowns.SetCooldown(cdKey, Math.Max(2000, slot.IntervalMs));
            ctx.Cooldowns.SetCooldown("attack_group", 2000); // 2s TFS attack group cooldown
            ctx.Cooldowns.TriggerGlobalActionCooldown(1000); // 1s global action exhaust

            return Protocol860Writer.Say(slot.SpellText, 1);
        }

        // Legacy fallback spell if enabled in config
        if (ctx.Config.EnableSpell && !string.IsNullOrWhiteSpace(ctx.Config.SpellText))
        {
            string legacyKey = "spell_legacy";
            if (ctx.Cooldowns.IsReady(legacyKey))
            {
                string spellLower = ctx.Config.SpellText.Trim().ToLowerInvariant();
                int maxRange = (spellLower == "exori") ? 1 : 4;

                if (distToTarget <= maxRange)
                {
                    ctx.Cooldowns.SetCooldown(legacyKey, (int)ctx.Config.SpellIntervalMs);
                    ctx.Cooldowns.TriggerGlobalActionCooldown(1000);
                    return Protocol860Writer.Say(ctx.Config.SpellText, 1);
                }
            }
        }

        return null;
    }
}
