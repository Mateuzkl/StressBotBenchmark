namespace StressBotBenchmark.Network;

// ProtocolGame::AddPlayerStats in this TFS fork, for client OS 2 / version 860.
// OTC/Astra and stock 8.60 use different layouts and need separate decoders.
public sealed record PlayerStats(uint Health, uint MaxHealth, uint Mana, uint MaxMana, ushort Level)
{
    public static PlayerStats Read(InputMessage message)
    {
        uint health = message.GetU32();
        uint maxHealth = message.GetU32();
        message.Skip(8); // capacity + experience
        ushort level = message.GetU16();
        message.Skip(1); // level percent
        uint mana = message.GetU32();
        uint maxMana = message.GetU32();
        message.Skip(5); // magic level, magic percent, soul, stamina (u16)
        return new(health, maxHealth, mana, maxMana, level);
    }
}
