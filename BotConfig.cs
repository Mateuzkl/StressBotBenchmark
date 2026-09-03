using System.Text.Json;
using System.Text.Json.Serialization;

namespace StressBotBenchmark
{
    // ── Vocação ─────────────────────────────────────────────
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Vocation
    {
        Knight,
        Paladin,
        Sorcerer,
        Druid
    }

    // ── Slot de Healing (HP ou Mana) ────────────────────────
    public class HealingSlot
    {
        public bool Enabled { get; set; } = false;
        public string SpellText { get; set; } = "";       // ex: "exura", "exura gran"
        public int ThresholdPercent { get; set; } = 60;    // casta quando HP/MP <= X%
        public int CooldownMs { get; set; } = 1000;
    }

    // ── Slot de Spell ofensiva ──────────────────────────────
    public class SpellSlot
    {
        public bool Enabled { get; set; } = false;
        public string SpellText { get; set; } = "";        // ex: "exori", "exevo gran mas flam"
        public int IntervalMs { get; set; } = 2000;
        public int MinManaPercent { get; set; } = 30;      // só casta se mana >= X%
    }

    public class ConsumablesConfig
    {
        public List<ushort> HealthPotionClientIds { get; set; } = new();
        public List<ushort> ManaPotionClientIds { get; set; } = new();
    }

    // ── Configuração de vocação ─────────────────────────────
    public class VocationProfile
    {
        public Vocation Vocation { get; set; } = Vocation.Knight;

        // Healing
        public HealingSlot Heal1 { get; set; } = new();   // heal leve
        public HealingSlot Heal2 { get; set; } = new();   // heal forte
        public HealingSlot HealMana { get; set; } = new(); // mana restore

        // Spells ofensivas (até 4)
        public SpellSlot Spell1 { get; set; } = new();
        public SpellSlot Spell2 { get; set; } = new();
        public SpellSlot Spell3 { get; set; } = new();
        public SpellSlot Spell4 { get; set; } = new();
    }

    // ── Config principal ────────────────────────────────────
    public class BotConfig
    {
        // ─── Conexão ────────────────────────────────────────
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 7172;

        // ─── Contas ─────────────────────────────────────────
        public int BotCount { get; set; } = 100;
        public string Prefix { get; set; } = "stressbot";
        public string Password { get; set; } = "test123";
        public int AccountWidth { get; set; } = 3;

        // ─── Login rate ─────────────────────────────────────
        public double LoginDelayMs { get; set; } = 650;
        public int BurstSize { get; set; } = 1;
        public double BurstPauseMs { get; set; } = 0;

        // ─── Vocação / Combate ──────────────────────────────
        public VocationProfile VocationConfig { get; set; } = new();

        // ─── Comportamento ──────────────────────────────────
        public bool EnableRandomWalk { get; set; } = false;
        public double WalkIntervalMs { get; set; } = 1500;

        public bool EnableChat { get; set; } = false;
        public double ChatIntervalMs { get; set; } = 5000;

        public bool EnableSpell { get; set; } = false;
        public double SpellIntervalMs { get; set; } = 5000;
        public string SpellText { get; set; } = "exevo gran mas flam";

        public bool EnableAttack { get; set; } = false;
        public double AttackScanIntervalMs { get; set; } = 2000;
        public bool EnableChaseMode { get; set; } = true;
        public byte FightMode { get; set; } = 1; // 1=Offensive 2=Balanced 3=Defensive
        public bool SafeFight { get; set; } = false;

        // ─── Sistema ────────────────────────────────────────
        public double DashboardIntervalMs { get; set; } = 1000;
        public bool LoginOnly { get; set; } = false;
        public bool Reconnect { get; set; } = true;
        public int QueueSize { get; set; } = 32;
        public int MaxSendLagMsToDrop { get; set; } = 1200;
        public double PingbackMinIntervalMs { get; set; } = 5000;
        // OS 2 / TFS 8.60 accepts periodic 0x1E heartbeats, including when its
        // ping is bundled after map data that the lightweight parser skips.
        public double KeepAliveIntervalMs { get; set; } = 5000;
        // Turn in place to reset TFS's separate idle timer. Zero disables it.
        public double IdleTurnIntervalMs { get; set; } = 60000;

        // ─── AI Simulator Config ────────────────────────────
        public bool AiEnabled { get; set; } = true;
        public int? RandomSeed { get; set; }
        public string BehaviorProfile { get; set; } = "mixed";
        public ConsumablesConfig Consumables { get; set; } = new();
        public List<string> ChatMessages { get; set; } = new();

        // ── Presets rápidos por vocação (alinhados com TFS 8.60) ──────
        public static VocationProfile PresetKnight() => new()
        {
            Vocation = Vocation.Knight,
            Heal1 = new() { Enabled = true, SpellText = "exura ico", ThresholdPercent = 75, CooldownMs = 2000 },
            Heal2 = new() { Enabled = true, SpellText = "exura gran ico", ThresholdPercent = 35, CooldownMs = 120000 }, // 120s cooldown no TFS
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exori", IntervalMs = 4000, MinManaPercent = 25 }, // 4s cooldown no TFS
            Spell2 = new() { Enabled = true, SpellText = "exori gran", IntervalMs = 6000, MinManaPercent = 40 }, // 6s cooldown no TFS
            Spell3 = new() { Enabled = true, SpellText = "exori hur", IntervalMs = 6000, MinManaPercent = 15 },
        };

        public static VocationProfile PresetPaladin() => new()
        {
            Vocation = Vocation.Paladin,
            Heal1 = new() { Enabled = true, SpellText = "exura", ThresholdPercent = 75, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura san", ThresholdPercent = 45, CooldownMs = 1000 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exori con", IntervalMs = 2000, MinManaPercent = 20 },
            Spell2 = new() { Enabled = true, SpellText = "exevo mas san", IntervalMs = 4000, MinManaPercent = 40 }, // 4s cooldown no TFS
        };

        public static VocationProfile PresetSorcerer() => new()
        {
            Vocation = Vocation.Sorcerer,
            Heal1 = new() { Enabled = true, SpellText = "exura", ThresholdPercent = 70, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura vita", ThresholdPercent = 40, CooldownMs = 1000 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exori vis", IntervalMs = 2000, MinManaPercent = 20 },
            Spell2 = new() { Enabled = true, SpellText = "exevo vis lux", IntervalMs = 4000, MinManaPercent = 35 },
            Spell3 = new() { Enabled = true, SpellText = "exevo gran mas flam", IntervalMs = 40000, MinManaPercent = 60 }, // 40s UE no TFS
        };

        public static VocationProfile PresetDruid() => new()
        {
            Vocation = Vocation.Druid,
            Heal1 = new() { Enabled = true, SpellText = "exura", ThresholdPercent = 70, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura gran", ThresholdPercent = 45, CooldownMs = 1000 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exori frigo", IntervalMs = 2000, MinManaPercent = 20 },
            Spell2 = new() { Enabled = true, SpellText = "exevo frigo hur", IntervalMs = 4000, MinManaPercent = 30 },
            Spell3 = new() { Enabled = true, SpellText = "exevo gran mas frigo", IntervalMs = 40000, MinManaPercent = 60 }, // 40s UE no TFS
        };
    }
}
