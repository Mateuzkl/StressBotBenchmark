using System.Text;

namespace StressBotBenchmark.Protocol;

public enum ActionKind { Other, Walk, Turn, Attack, Spell, Heal, Potion, Chat, Outfit }

public static class ActionClassifier
{
    // Only stateless movement/turn/target refreshes may expire before writing.
    // Login, logout, ping, inventory actions and spell/chat text are never dropped.
    public static bool IsReplaceable(ReadOnlySpan<byte> payload) => !payload.IsEmpty &&
        (payload[0] == 0x64 || payload[0] is >= 0x65 and <= 0x68 ||
         payload[0] is >= 0x6A and <= 0x6D || payload[0] is >= 0x6F and <= 0x72 ||
         (payload[0] == 0xA1 && payload.Length >= 5 && BitConverter.ToUInt32(payload[1..5]) != 0));

    public static ActionKind Classify(ReadOnlySpan<byte> payload, BotConfig config)
    {
        if (payload.IsEmpty) return ActionKind.Other;
        byte opcode = payload[0];
        if (opcode == 0x64 || opcode is >= 0x65 and <= 0x68 || opcode is >= 0x6A and <= 0x6D)
            return ActionKind.Walk;
        if (opcode is >= 0x6F and <= 0x72) return ActionKind.Turn;
        if (opcode == 0xA1 && payload.Length >= 5 && BitConverter.ToUInt32(payload[1..5]) != 0)
            return ActionKind.Attack;
        if (opcode == 0x84) return ActionKind.Potion;
        if (opcode == 0xD3) return ActionKind.Outfit;
        if (opcode != 0x96 || payload.Length < 4) return ActionKind.Other;
        int length = BitConverter.ToUInt16(payload[2..4]);
        if (length > payload.Length - 4) return ActionKind.Other;
        string text = Encoding.Latin1.GetString(payload.Slice(4, length));
        var v = config.VocationConfig;
        if (new[] { v.Heal1, v.Heal2, v.HealMana }.Any(h => h.Enabled &&
            text.Equals(h.SpellText, StringComparison.OrdinalIgnoreCase))) return ActionKind.Heal;
        if ((config.EnableSpell && text.Equals(config.SpellText, StringComparison.OrdinalIgnoreCase)) ||
            new[] { v.Spell1, v.Spell2, v.Spell3, v.Spell4 }.Any(s => s.Enabled &&
                text.Equals(s.SpellText, StringComparison.OrdinalIgnoreCase))) return ActionKind.Spell;
        return ActionKind.Chat;
    }
}
