using StressBotBenchmark.AI;
using StressBotBenchmark.Data;
using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;
using StressBotBenchmark.World;

namespace StressBotBenchmark;

internal static class SelfTests
{
    public static void Run()
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        Check(StableSeed.For("bot_1", 42) == 1034264140, "Cross-process seed changed.");
        Check(StableSeed.For("bot_1", 42) != StableSeed.For("bot_2", 42), "Bots share a seed.");
        int idle = 0, combat = 0;
        for (int bot = 0; bot < 1000; bot++)
        {
            int seed = StableSeed.For($"bot_{bot}", 42);
            var first = new WorkloadSchedule(new ActivityWeights(), seed);
            var second = new WorkloadSchedule(new ActivityWeights(), seed);
            for (int ms = 0; ms <= 120000; ms += 1000)
            {
                first.Advance(ms); second.Advance(ms);
                Check(first.State == second.State, "Seeded activity is not repeatable.");
                if (first.State == ActivityState.Idle) idle++;
                if (first.State == ActivityState.Combat) combat++;
            }
        }
        Check(idle > combat && combat > 0, "Realistic population did not rest or fight.");
        var config = new BotConfig { LoginOnly = true, EnableAttack = true, EnableOutfit = true };
        Check(new BotBrain(new WorldState(), config, 42).Tick() == null, "Login-only generated gameplay.");
        Check(config.EffectiveAiTickMinMs == 500 && config.EffectiveAiTickMaxMs == 1000, "Realistic tick mismatch.");
        config.LoginOnly = false;
        config.WorkloadMode = WorkloadMode.TORTURE;
        Check(config.EffectiveAiTickMinMs == 175, "Torture tick mismatch.");
        config.AiTickIntervalMinMs = 800;
        Check(config.EffectiveAiTickMinMs == 800, "Configured tick ignored.");
        config.VocationConfig.Heal1 = new HealingSlot { Enabled = true, SpellText = "exura" };
        Check(ActionClassifier.Classify(Protocol860Writer.Say("exura", 1).GetBuffer(), config) == ActionKind.Heal, "Heal counted as spell.");
        Check(ActionClassifier.Classify(Protocol860Writer.Say("hello", 1).GetBuffer(), config) == ActionKind.Chat, "Chat counted as spell.");
        Check(!ActionClassifier.IsReplaceable(Protocol860Writer.Attack(0).GetBuffer()), "Target cancellation may expire.");
        Check(!ActionClassifier.IsReplaceable(new byte[] { 0x14 }) &&
              !ActionClassifier.IsReplaceable(new byte[] { 0x1E }) &&
              !ActionClassifier.IsReplaceable(new byte[] { 0x84 }), "Critical action may expire.");
        Check(ActionClassifier.IsReplaceable(Protocol860Writer.Attack(123).GetBuffer()), "Attack refresh not bounded.");

        var world = new WorldState();
        var map = new InputMessage(new byte[] { 106, 0, 10, 255, 106, 0, 4, 255, 0x1E });
        MapParser.ParseMapSlice(map, world, 100, 100, 7, 2, 1);
        Check(map.Remaining == 1 && map.GetU8() == 0x1E, "Cross-floor skips consumed the next opcode.");
        Check(world.GetTile(100, 100, 7)?.GroundId == 106 &&
              world.GetTile(106, 105, 2)?.GroundId == 106, "Cross-floor skip positions are wrong.");
        var metrics = new BotMetrics();
        bool ping = false;
        var parser = new Protocol860Parser(world, metrics) { OnPingReceived = () => ping = true };
        parser.ProcessPayload(new InputMessage(new byte[] { 0x6B, 100, 0, 100, 0, 7, 1, 0x63, 0, 1, 0, 0, 0, 2, 0x1E }), true);
        Check(ping && metrics.ParserErrors == 0 && metrics.UnknownOpcodes == 0, "Creature turn desynchronized the payload.");
        ping = false;
        parser.ProcessPayload(new InputMessage(new byte[] { 0x1D }), true);
        Check(!ping, "Ping acknowledgement generated an echo.");
        parser.ProcessPayload(new InputMessage(new byte[] { 0x84, 100, 0, 100, 0, 7, 200, 1, 0, 49, 0x1E }), true);
        Check(ping && metrics.UnknownOpcodes == 0, "Animated text blocked the following ping.");
        var outfits = new OutputMessage();
        outfits.AddU8(0xC8); outfits.AddU16(128); outfits.AddBytes(new byte[5]); outfits.AddU16(0);
        outfits.AddU16(1); outfits.AddU16(128); outfits.AddString("Citizen"); outfits.AddU8(3);
        outfits.AddU8(0); outfits.AddU8(0x1E);
        parser.ProcessPayload(new InputMessage(outfits.GetBuffer()), true);
        Check(parser.AvailableOutfits?.Count == 1 && metrics.ParserErrors == 0, "Non-OTC u16 outfit count misparsed.");
        parser.ProcessPayload(new InputMessage(new byte[] { 0x64, 100, 0, 100, 0, 7, 106, 0 }), true);
        Check(metrics.ParserErrors == 1 && metrics.UnknownOpcodes == 0, "Truncated map did not fail at its boundary.");
        var tile = new TileState(100, 100, 7);
        tile.AddItem(106); tile.AddCreature(123); tile.AddItem(3031);
        Check(tile.RemoveThing(1) == 123 && tile.Things[1].ItemId == 3031, "Tile stack indices are wrong.");
        metrics.AddQueueWaitMs(0.2); metrics.AddQueueWaitMs(11);
        Check(metrics.QueueP95Ms == 25 && metrics.QueueP99Ms == 25, "Histogram quantiles incorrect.");
        try { ItemCatalog.Parse(new byte[8]); throw new InvalidOperationException("Invalid OTB accepted."); }
        catch (InvalidDataException) { }
        TestSendAdmission().GetAwaiter().GetResult();
        Console.WriteLine("Self-tests passed: seeded schedules (1000 bots), mode/timing, action classification, map skips, creature turns, truncation, tile stacks and latency histograms.");
    }

    private static async Task TestSendAdmission()
    {
        var config = new BotConfig { QueueSize = 1, MaxSendLagMsToDrop = 50 };
        var metrics = new BotMetrics();
        using var pacer = new ConnectionPacer(650);
        using var bot = new TibiaBot("self-test", "unused", config, metrics, pacer);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var writeLock = (SemaphoreSlim)typeof(TibiaBot).GetField("_writeLock", flags)!.GetValue(bot)!;
        var write = typeof(TibiaBot).GetMethod("WritePacketAsync", flags)!;
        await writeLock.WaitAsync();
        try
        {
            Task<bool> Submit(bool replaceable, CancellationToken token) => (Task<bool>)write.Invoke(bot,
                new object[] { new byte[] { 0 }, token, false, false, replaceable })!;
            var queued = Submit(true, CancellationToken.None);
            if (await Submit(true, CancellationToken.None)) throw new InvalidOperationException("Queue overflow was sent.");
            if (await queued || metrics.QueueFull != 1 || metrics.Dropped != 2 || metrics.Sent != 0)
                throw new InvalidOperationException("Production send admission ignored its capacity or age limit.");
            if (metrics.StaleDropped != 1 || metrics.MaxQueueWaitMs < config.MaxSendLagMsToDrop || metrics.QueueP99Ms < config.MaxSendLagMsToDrop)
                throw new InvalidOperationException("Stale queue waits were omitted from telemetry.");
            using var cancel = new CancellationTokenSource(100);
            try { await Submit(false, cancel.Token); throw new InvalidOperationException("Blocked critical write completed."); }
            catch (OperationCanceledException) { }
            if (metrics.Dropped != 2) throw new InvalidOperationException("Critical write was dropped as stale.");
        }
        finally { writeLock.Release(); }
    }
}
