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
        public double LoginDelayMs { get; set; } = 100;
        public int BurstSize { get; set; } = 5;
        public double BurstPauseMs { get; set; } = 1000;

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

        // ── Presets rápidos por vocação ──────────────────────
        public static VocationProfile PresetKnight() => new()
        {
            Vocation = Vocation.Knight,
            Heal1 = new() { Enabled = true, SpellText = "exura ico", ThresholdPercent = 70, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura gran ico", ThresholdPercent = 40, CooldownMs = 1200 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exori", IntervalMs = 2000, MinManaPercent = 20 },
            Spell2 = new() { Enabled = true, SpellText = "exori gran", IntervalMs = 4000, MinManaPercent = 30 },
        };

        public static VocationProfile PresetPaladin() => new()
        {
            Vocation = Vocation.Paladin,
            Heal1 = new() { Enabled = true, SpellText = "exura", ThresholdPercent = 70, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura gran", ThresholdPercent = 40, CooldownMs = 1200 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exori con", IntervalMs = 2000, MinManaPercent = 20 },
            Spell2 = new() { Enabled = true, SpellText = "exevo mas san", IntervalMs = 4000, MinManaPercent = 40 },
        };

        public static VocationProfile PresetSorcerer() => new()
        {
            Vocation = Vocation.Sorcerer,
            Heal1 = new() { Enabled = true, SpellText = "exura", ThresholdPercent = 65, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura vita", ThresholdPercent = 35, CooldownMs = 1200 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exevo vis lux", IntervalMs = 2000, MinManaPercent = 25 },
            Spell2 = new() { Enabled = true, SpellText = "exevo gran mas flam", IntervalMs = 4000, MinManaPercent = 50 },
        };

        public static VocationProfile PresetDruid() => new()
        {
            Vocation = Vocation.Druid,
            Heal1 = new() { Enabled = true, SpellText = "exura", ThresholdPercent = 70, CooldownMs = 1000 },
            Heal2 = new() { Enabled = true, SpellText = "exura gran", ThresholdPercent = 40, CooldownMs = 1200 },
            HealMana = new(),
            Spell1 = new() { Enabled = true, SpellText = "exevo gran mas frigo", IntervalMs = 2000, MinManaPercent = 25 },
            Spell2 = new() { Enabled = true, SpellText = "exura gran mas res", IntervalMs = 6000, MinManaPercent = 60 },
        };
    }
}
