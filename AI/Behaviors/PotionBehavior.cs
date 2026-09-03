using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;

namespace StressBotBenchmark.AI.Behaviors;

/// <summary>
/// Uses real potions on the player using 0x84 (UseWithCreature) when configured in inventory.
/// </summary>
public sealed class PotionBehavior
{
    public OutputMessage? Evaluate(DecisionContext ctx)
    {
        if (!ctx.Cooldowns.IsReady("potion_cd"))
            return null;

        var player = ctx.World.Player;
        if (player.MaxHp == 0) return null;

        var inv = ctx.World.Inventory;

        // Health potion check if HP < 45%
        if (player.HpPercent < 45.0)
        {
            foreach (var hpId in ctx.Config.Consumables.HealthPotionClientIds)
            {
                var itemLoc = inv.FindItem(hpId);
                if (itemLoc.HasValue)
                {
                    ctx.Cooldowns.SetCooldown("potion_cd", 1000); // 1s potion cooldown
                    // 0xFFFF = inventory position, stackpos = slot, creatureId = player.Id
                    return Protocol860Writer.UseWithCreature(0xFFFF, itemLoc.Value.Cid, 0, hpId, (byte)itemLoc.Value.Slot, player.Id);
                }
            }
        }

        // Mana potion check if Mana < 40%
        if (player.ManaPercent < 40.0)
        {
            foreach (var manaId in ctx.Config.Consumables.ManaPotionClientIds)
            {
                var itemLoc = inv.FindItem(manaId);
                if (itemLoc.HasValue)
                {
                    ctx.Cooldowns.SetCooldown("potion_cd", 1000);
                    return Protocol860Writer.UseWithCreature(0xFFFF, itemLoc.Value.Cid, 0, manaId, (byte)itemLoc.Value.Slot, player.Id);
                }
            }
        }

        return null;
    }
}
