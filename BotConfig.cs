namespace StressBotBenchmark
{
    public class BotConfig
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 7172;

        public int BotCount { get; set; } = 100;
        public string Prefix { get; set; } = "stressbot";
        public string Password { get; set; } = "test123";
        public int AccountWidth { get; set; } = 3;
        
        public double LoginDelayMs { get; set; } = 15;
        public int BurstSize { get; set; } = 20;
        public double BurstPauseMs { get; set; } = 300;
        
        public double WalkIntervalMs { get; set; } = 1500;
        public double ChatIntervalMs { get; set; } = 5000;
        public double SpellIntervalMs { get; set; } = 5000;
        public double AttackScanIntervalMs { get; set; } = 2000;
        
        public string SpellText { get; set; } = "exevo gran mas flam";
        public bool EnableRandomWalk { get; set; } = false;
        public bool EnableChat { get; set; } = false;
        public bool EnableSpell { get; set; } = false;
        public bool EnableAttack { get; set; } = false;
        public bool EnableChaseMode { get; set; } = true;
        
        public byte FightMode { get; set; } = 1; // 1 = Offensive
        public bool SafeFight { get; set; } = false;
        
        public double DashboardIntervalMs { get; set; } = 1000;
        public bool LoginOnly { get; set; } = false;
        public bool Reconnect { get; set; } = true;
        
        public int QueueSize { get; set; } = 32;
        public int MaxSendLagMsToDrop { get; set; } = 1200;
        public double PingbackMinIntervalMs { get; set; } = 5000;
    }
}
