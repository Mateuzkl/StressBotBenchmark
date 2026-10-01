using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using Spectre.Console;

namespace StressBotBenchmark
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Contains("--self-test"))
            {
                SelfTests.Run();
                return;
            }
            AnsiConsole.Write(new FigletText("StressBot 8.60").Color(Color.Yellow));
            AnsiConsole.MarkupLine("[grey]Tibia 8.60 StressBot Cluster - Console Edition[/]\n");

            BotConfig config;

            // ── Se passou argumento --script=nome, pula o menu ──
            string? scriptArg = args.FirstOrDefault(a => a.StartsWith("--script="));
            if (scriptArg != null)
            {
                string scriptName = scriptArg.Substring("--script=".Length);
                config = ScriptManager.Load(scriptName);
                AnsiConsole.MarkupLine($"[green]Script '{scriptName}' carregado![/]");
            }
            else if (args.Length >= 1 && int.TryParse(args[0], out int countArg))
            {
                config = new BotConfig { BotCount = countArg };
            }
            else
            {
                config = RunInteractiveMenu();
            }

            await RunBenchmarkAsync(config, args);
        }

        static async Task RunBenchmarkAsync(BotConfig config, string[] args)
        {
            double durationSeconds = 0;
            try
            {
                foreach (string arg in args)
                {
                    if (arg.StartsWith("--bots=")) config.BotCount = int.Parse(arg[7..], CultureInfo.InvariantCulture);
                    else if (arg == "--login-only") config.LoginOnly = true;
                    else if (arg.StartsWith("--mode="))
                    {
                        config.WorkloadMode = Enum.Parse<WorkloadMode>(arg[7..], ignoreCase: true);
                        config.LoginOnly = false;
                    }
                    else if (arg.StartsWith("--seed=")) config.RandomSeed = int.Parse(arg[7..], CultureInfo.InvariantCulture);
                    else if (arg.StartsWith("--items-otb=")) config.ItemsOtbPath = arg[12..];
                    else if (arg.StartsWith("--duration="))
                    {
                        string value = arg[11..];
                        double multiplier = value.EndsWith('m') ? 60 : value.EndsWith('h') ? 3600 : 1;
                        if (value.EndsWith('s') || value.EndsWith('m') || value.EndsWith('h')) value = value[..^1];
                        durationSeconds = double.Parse(value, CultureInfo.InvariantCulture) * multiplier;
                    }
                }
                if (config.BotCount < 1 || config.BotCount > 1000 || config.AccountWidth < 1 || config.AccountWidth > 12 ||
                    string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.Prefix) ||
                    config.Port < 1 || config.Port > 65535 ||
                    !double.IsFinite(config.LoginDelayMs) || config.LoginDelayMs < 0 || config.LoginDelayMs > 60000 ||
                    !double.IsFinite(config.KeepAliveIntervalMs) || (config.KeepAliveIntervalMs != 0 && config.KeepAliveIntervalMs < 1000) || config.KeepAliveIntervalMs > 5000 ||
                    !double.IsFinite(config.IdleTurnIntervalMs) || config.IdleTurnIntervalMs < 0 || config.IdleTurnIntervalMs > 600000 ||
                    !double.IsFinite(config.DashboardIntervalMs) || config.DashboardIntervalMs < 100 || config.DashboardIntervalMs > 60000 ||
                    !double.IsFinite(durationSeconds) || durationSeconds < 0 || durationSeconds > 86400 * 7 ||
                    !Enum.IsDefined(config.WorkloadMode) || config.QueueSize < 1 || config.QueueSize > 1024 ||
                    config.MaxSendLagMsToDrop < 1 || config.MaxSendLagMsToDrop > 60000 ||
                    !double.IsFinite(config.MaxPacketsPerSecondPerBot) || config.MaxPacketsPerSecondPerBot < 1 || config.MaxPacketsPerSecondPerBot > 20 ||
                    config.EffectiveAiTickMinMs < 50 || config.EffectiveAiTickMaxMs < config.EffectiveAiTickMinMs || config.EffectiveAiTickMaxMs > 60000 ||
                    !double.IsFinite(config.PingbackMinIntervalMs) || config.PingbackMinIntervalMs < 0 || config.PingbackMinIntervalMs > 60000 ||
                    new[] { config.WalkIntervalMs, config.AttackScanIntervalMs, config.ChatIntervalMs, config.SpellIntervalMs }
                        .Any(ms => !double.IsFinite(ms) || ms < 1 || ms > 600000))
                    throw new ArgumentException("Configuração inválida: bots 1–1000, ping 1000–5000 ms e intervalos válidos são necessários.");
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
                Environment.ExitCode = 2;
                return;
            }

            if (config.LoginDelayMs < 650)
                AnsiConsole.MarkupLine("[yellow]Login rápido: use somente em servidor privado com rate limit compatível.[/]");
            if (config.EffectiveWorkloadMode == WorkloadMode.LOGIN_ONLY)
            {
                config.EnableAttack = config.EnableRandomWalk = config.EnableChat = config.EnableSpell = false;
            }
            ShowConfigSummary(config);
            AnsiConsole.MarkupLine($"[grey]Mode: {config.EffectiveWorkloadMode}; seed: {config.RandomSeed?.ToString() ?? "random"}; AI tick: {config.EffectiveAiTickMinMs}–{config.EffectiveAiTickMaxMs} ms; packet cap: {config.MaxPacketsPerSecondPerBot}/s/bot.[/]");
            try
            {
                _ = new AI.WorkloadSchedule(config.ActivityWeights, 0);
                if (!string.IsNullOrWhiteSpace(config.ItemsOtbPath)) Data.ItemCatalog.Load(config.ItemsOtbPath);
            }
            catch (Exception error) when (error is IOException or ArgumentException)
            {
                Console.Error.WriteLine(error.Message);
                Environment.ExitCode = 2;
                return;
            }
            if (Data.ItemCatalog.Current == null)
                AnsiConsole.MarkupLine("[yellow]Sem itemsOtbPath: parsing de itens é heurístico; resultados não validam mapas com itens customizados.[/]");
            else AnsiConsole.MarkupLine($"[grey]OTB metadata: {Data.ItemCatalog.Current.Count} items.[/]");
            AnsiConsole.MarkupLine($"[grey]Intervalo global: {config.LoginDelayMs:F0} ms. Subida mínima: {(config.BotCount - 1) * config.LoginDelayMs / 60000:F1} min. Ctrl+C encerra.[/]");
            if (args.Contains("--check-config")) return;

            var metrics = new BotMetrics();
            using var stop = new CancellationTokenSource();
            if (durationSeconds > 0) stop.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
            using var pacer = new ConnectionPacer(config.LoginDelayMs);
            // Build the collection before starting tasks so the dashboard sees a
            // stable array, while each connection exposes its current state.
            var bots = Enumerable.Range(1, config.BotCount)
                .Select(i => new TibiaBot($"{config.Prefix}_{i.ToString($"D{config.AccountWidth}")}",
                    config.Password, config, metrics, pacer)).ToArray();
            ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += cancel;
            var elapsed = Stopwatch.StartNew();
            var runs = bots.Select(bot => bot.StartAsync(stop.Token)).ToArray();
            var allBots = Task.WhenAll(runs);
            var dashboard = DashboardLoopAsync(config, metrics, bots, stop.Token);
            try
            {
                await Task.WhenAny(allBots, dashboard);
            }
            finally
            {
                stop.Cancel();
                foreach (var bot in bots) bot.Stop();
                try { await allBots; }
                finally
                {
                    try { await dashboard; } catch (OperationCanceledException) { }
                    foreach (var bot in bots) bot.Dispose();
                    Console.CancelKeyPress -= cancel;
                }
            }
            AnsiConsole.MarkupLine($"[bold]Encerrado após {elapsed.Elapsed.TotalSeconds:F1}s. Disconnects: {metrics.Disconnects}; falhas TCP: {metrics.ConnectionFailures}; pings: {metrics.Pingbacks}.[/]");
        }

        // ════════════════════════════════════════════════════════
        //  MENU INTERATIVO
        // ════════════════════════════════════════════════════════
        static BotConfig RunInteractiveMenu()
        {
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold yellow]O que deseja fazer?[/]")
                    .AddChoices("Nova config", "Carregar script", "Config rápida (só login)"));

            if (action == "Carregar script")
                return LoadScriptMenu();
            if (action == "Config rápida (só login)")
                return QuickLoginConfig();

            var config = new BotConfig();

            // ── Conexão ──
            AnsiConsole.MarkupLine("\n[bold teal]── Conexão ──[/]");
            config.Host = AnsiConsole.Ask("Host:", config.Host);
            config.Port = AnsiConsole.Ask("Port:", config.Port);
            config.BotCount = AnsiConsole.Ask("Quantidade de bots:", config.BotCount);
            config.Prefix = AnsiConsole.Ask("Prefixo da conta:", config.Prefix);
            config.Password = AnsiConsole.Ask("Senha:", config.Password);

            // ── Vocação ──
            AnsiConsole.MarkupLine("\n[bold teal]── Vocação ──[/]");
            var voc = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Vocação dos bots:")
                    .AddChoices("Knight", "Paladin", "Sorcerer", "Druid", "Custom (manual)"));

            if (voc != "Custom (manual)")
            {
                config.VocationConfig = voc switch
                {
                    "Knight" => BotConfig.PresetKnight(),
                    "Paladin" => BotConfig.PresetPaladin(),
                    "Sorcerer" => BotConfig.PresetSorcerer(),
                    "Druid" => BotConfig.PresetDruid(),
                    _ => new VocationProfile()
                };
                AnsiConsole.MarkupLine($"[green]Preset {voc} carregado com heals e spells padrão.[/]");
            }
            else
            {
                config.VocationConfig = ConfigureVocationManual();
            }

            // ── Comportamento ──
            AnsiConsole.MarkupLine("\n[bold teal]── Comportamento ──[/]");

            var behaviors = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Habilitar:")
                    .InstructionsText("[grey](Espaço = toggle, Enter = confirmar)[/]")
                    .AddChoices("Atacar monstros", "Andar aleatório", "Chat", "Login-only (idle)"));

            config.EnableAttack = behaviors.Contains("Atacar monstros");
            config.EnableRandomWalk = behaviors.Contains("Andar aleatório");
            config.EnableChat = behaviors.Contains("Chat");
            config.EnableSpell = config.VocationConfig.Spell1.Enabled ||
                                 config.VocationConfig.Spell2.Enabled ||
                                 config.VocationConfig.Spell3.Enabled ||
                                 config.VocationConfig.Spell4.Enabled;

            // LoginOnly só se nenhuma ação foi selecionada, ou se explicitamente escolheu idle
            bool anyAction = config.EnableAttack || config.EnableRandomWalk || config.EnableChat || config.EnableSpell;
            config.LoginOnly = behaviors.Contains("Login-only (idle)") || !anyAction;

            if (config.EnableAttack)
            {
                config.FightMode = (byte)AnsiConsole.Prompt(
                    new SelectionPrompt<int>()
                        .Title("Fight Mode:")
                        .AddChoices(1, 2, 3)
                        .UseConverter(m => m switch { 1 => "Offensive", 2 => "Balanced", 3 => "Defensive", _ => "?" }));
                config.EnableChaseMode = AnsiConsole.Confirm("Chase Mode?", true);
            }

            // ── Salvar como script? ──
            if (AnsiConsole.Confirm("\n[yellow]Salvar esta config como script?[/]", true))
            {
                string name = AnsiConsole.Ask<string>("Nome do script:");
                string path = ScriptManager.Save(config, name);
                AnsiConsole.MarkupLine($"[green]Salvo em:[/] {path}");
            }

            return config;
        }

        static VocationProfile ConfigureVocationManual()
        {
            var vp = new VocationProfile();
            AnsiConsole.MarkupLine("\n[bold]Configuração manual de spells e heals:[/]");

            // Heal 1
            if (AnsiConsole.Confirm("Heal 1 (heal leve)?", true))
            {
                vp.Heal1.Enabled = true;
                vp.Heal1.SpellText = AnsiConsole.Ask("Spell:", "exura");
                vp.Heal1.ThresholdPercent = AnsiConsole.Ask("HP% para castar:", 70);
                vp.Heal1.CooldownMs = AnsiConsole.Ask("Cooldown (ms):", 1000);
            }

            // Heal 2
            if (AnsiConsole.Confirm("Heal 2 (heal forte/emergência)?", false))
            {
                vp.Heal2.Enabled = true;
                vp.Heal2.SpellText = AnsiConsole.Ask("Spell:", "exura gran");
                vp.Heal2.ThresholdPercent = AnsiConsole.Ask("HP% para castar:", 40);
                vp.Heal2.CooldownMs = AnsiConsole.Ask("Cooldown (ms):", 1200);
            }

            // Spell 1
            if (AnsiConsole.Confirm("Spell ofensiva 1?", true))
            {
                vp.Spell1.Enabled = true;
                vp.Spell1.SpellText = AnsiConsole.Ask("Spell:", "exori");
                vp.Spell1.IntervalMs = AnsiConsole.Ask("Intervalo (ms):", 2000);
            }

            // Spell 2
            if (AnsiConsole.Confirm("Spell ofensiva 2?", false))
            {
                vp.Spell2.Enabled = true;
                vp.Spell2.SpellText = AnsiConsole.Ask("Spell:", "exevo gran mas flam");
                vp.Spell2.IntervalMs = AnsiConsole.Ask("Intervalo (ms):", 4000);
            }

            return vp;
        }

        static BotConfig LoadScriptMenu()
        {
            var scripts = ScriptManager.ListScripts();
            if (scripts.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]Nenhum script salvo. Criando config nova...[/]");
                return RunInteractiveMenu();
            }

            var chosen = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Selecione o script:[/]")
                    .AddChoices(scripts));

            var config = ScriptManager.Load(chosen);
            AnsiConsole.MarkupLine($"[green]Script '{chosen}' carregado![/]");
            return config;
        }

        static BotConfig QuickLoginConfig()
        {
            var config = new BotConfig
            {
                LoginOnly = true,
                EnableAttack = false,
                EnableSpell = false,
                EnableRandomWalk = false,
                EnableChat = false
            };
            config.Host = AnsiConsole.Ask("Host:", config.Host);
            config.Port = AnsiConsole.Ask("Port:", config.Port);
            config.BotCount = AnsiConsole.Ask("Quantidade:", config.BotCount);
            config.Prefix = AnsiConsole.Ask("Prefixo:", config.Prefix);
            config.Password = AnsiConsole.Ask("Senha:", config.Password);

            if (AnsiConsole.Confirm("[yellow]Salvar como script?[/]", false))
            {
                string name = AnsiConsole.Ask<string>("Nome:");
                ScriptManager.Save(config, name);
            }
            return config;
        }

        static void ShowConfigSummary(BotConfig c)
        {
            var vp = c.VocationConfig;
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[teal]Setting[/]");
            table.AddColumn("[teal]Value[/]");

            table.AddRow("Vocação", $"[bold]{vp.Vocation}[/]");
            table.AddRow("Bots", $"{c.BotCount}");
            table.AddRow("Contas", Markup.Escape($"{c.Prefix}_{1.ToString($"D{c.AccountWidth}")} ... {c.Prefix}_{c.BotCount.ToString($"D{c.AccountWidth}")}"));
            table.AddRow("Ping / virar", $"{c.KeepAliveIntervalMs:F0} ms / {c.IdleTurnIntervalMs:F0} ms");
            table.AddRow("Modo", c.EffectiveWorkloadMode.ToString());
            table.AddRow("Atacar", c.EnableAttack ? "[green]Sim[/]" : "[grey]Não[/]");
            table.AddRow("Andar", c.EnableRandomWalk ? "[green]Sim[/]" : "[grey]Não[/]");
            table.AddRow("Spells", c.EnableSpell ? "[green]Sim[/]" : "[grey]Não[/]");

            if (!c.LoginOnly && vp.Spell1.Enabled) table.AddRow("Atk Spell 1", Markup.Escape($"{vp.Spell1.SpellText} cada {vp.Spell1.IntervalMs}ms"));
            if (!c.LoginOnly && vp.Spell2.Enabled) table.AddRow("Atk Spell 2", Markup.Escape($"{vp.Spell2.SpellText} cada {vp.Spell2.IntervalMs}ms"));

            AnsiConsole.Write(table);
        }

        // ════════════════════════════════════════════════════════
        //  LAUNCH + DASHBOARD (inalterados na lógica)
        // ════════════════════════════════════════════════════════

        static async Task DashboardLoopAsync(BotConfig config, BotMetrics metrics, IReadOnlyList<TibiaBot> bots, CancellationToken token)
        {
            if (Console.IsOutputRedirected)
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                using var process = Process.GetCurrentProcess();
                long previous = Stopwatch.GetTimestamp();
                var cpu = process.TotalProcessorTime;
                int previousActions = metrics.Actions, previousPackets = metrics.Sent;
                long previousIn = metrics.BytesIn, previousOut = metrics.BytesOut;
                while (!token.IsCancellationRequested)
                {
                    long now = Stopwatch.GetTimestamp();
                    double seconds = Math.Max(0.001, Stopwatch.GetElapsedTime(previous, now).TotalSeconds);
                    process.Refresh();
                    var currentCpu = process.TotalProcessorTime;
                    int inWorld = bots.Count(b => b.InWorld);
                    double actionsPerBot = inWorld == 0 ? 0 : (metrics.Actions - previousActions) / seconds / inWorld;
                    double packetsPerSecond = (metrics.Sent - previousPackets) / seconds;
                    string activity = config.EffectiveWorkloadMode == WorkloadMode.TORTURE
                        ? $"ActiveContinuous={inWorld}"
                        : $"Idle={bots.Count(b => b.InWorld && b.Activity == AI.ActivityState.Idle)} Walking={bots.Count(b => b.InWorld && b.Activity == AI.ActivityState.Walking)} " +
                          $"Combat={bots.Count(b => b.InWorld && b.Activity == AI.ActivityState.Combat)} Social={bots.Count(b => b.InWorld && b.Activity == AI.ActivityState.Social)}";
                    Console.WriteLine($"Mode={config.EffectiveWorkloadMode} Seed={config.RandomSeed} " +
                        $"ActionsPerSecPerBot={actionsPerBot:F3} PacketsPerSec={packetsPerSecond:F1} PacketsPerSecPerBot={(inWorld == 0 ? 0 : packetsPerSecond / inWorld):F3} " +
                        $"BytesInPerSec={(metrics.BytesIn - previousIn) / seconds:F1} BytesOutPerSec={(metrics.BytesOut - previousOut) / seconds:F1} " +
                        $"BotCpuOneCore={(currentCpu - cpu).TotalSeconds / seconds * 100:F2} BotRssMiB={process.WorkingSet64 / 1048576.0:F2} " +
                        $"{activity} QueueAvgMs={metrics.AvgQueueWaitMs:F3} QueueMaxMs={metrics.MaxQueueWaitMs:F3} " +
                        $"QueueP95Ms={metrics.QueueP95Ms} QueueP99Ms={metrics.QueueP99Ms} " +
                        $"SendAvgMs={metrics.AvgDrainMs:F3} SendMaxMs={metrics.MaxSendLagMs:F3} SendP95Ms={metrics.SendP95Ms} SendP99Ms={metrics.SendP99Ms}");
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} InWorld={bots.Count(b => b.InWorld)}/{config.BotCount} TCP={metrics.ConnectedCount} Failures={metrics.ConnectionFailures} Disconnects={metrics.Disconnects} Reconnects={metrics.Reconnects} Ping={metrics.Pingbacks} Turns={metrics.Turns} " +
                        $"PacketsIn={metrics.PacketsIn} PacketsOut={metrics.Sent} BytesIn={metrics.BytesIn} BytesOut={metrics.BytesOut} " +
                        $"Walks={metrics.Walks} Attacks={metrics.Attacks} Spells={metrics.Spells} Heals={metrics.Heals} Potions={metrics.Potions} Chats={metrics.Chats} Outfits={metrics.Outfits} " +
                        $"Dropped={metrics.Dropped} StaleDropped={metrics.StaleDropped} QueueFull={metrics.QueueFull} ParserErrors={metrics.ParserErrors} UnknownOpcodes={metrics.UnknownOpcodes} UnknownSummary={metrics.UnknownSummary} LastParserError={metrics.LastParserError ?? "none"} LastError={metrics.LastError ?? "none"}");
                    previous = now; cpu = currentCpu;
                    previousActions = metrics.Actions; previousPackets = metrics.Sent;
                    previousIn = metrics.BytesIn; previousOut = metrics.BytesOut;
                    await Task.Delay(TimeSpan.FromMilliseconds(config.DashboardIntervalMs), token);
                }
                return;
            }
            long lastBytesIn = 0;
            long lastBytesOut = 0;
            int lastPacketsIn = 0;
            int lastActions = 0;

            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            TimeSpan lastCpuTime = proc.TotalProcessorTime;
            DateTime lastTime = DateTime.UtcNow;

            await AnsiConsole.Live(new Panel("Initializing..."))
                .StartAsync(async ctx =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay((int)config.DashboardIntervalMs, token);

                        long bytesInNow = metrics.BytesIn;
                        long bytesOutNow = metrics.BytesOut;
                        int packetsInNow = metrics.PacketsIn;

                        long bytesInSec = bytesInNow - lastBytesIn;
                        long bytesOutSec = bytesOutNow - lastBytesOut;
                        int packetsInSec = packetsInNow - lastPacketsIn;

                        lastBytesIn = bytesInNow;
                        lastBytesOut = bytesOutNow;
                        lastPacketsIn = packetsInNow;

                        proc.Refresh();

                        TimeSpan cpuTime = proc.TotalProcessorTime;
                        DateTime now = DateTime.UtcNow;
                        double sampleSeconds = Math.Max(0.001, (now - lastTime).TotalSeconds);
                        double cpuUsage = (cpuTime - lastCpuTime).TotalMilliseconds / (now - lastTime).TotalMilliseconds / Environment.ProcessorCount * 100.0;
                        lastCpuTime = cpuTime;
                        lastTime = now;
                        double ramMb = proc.PrivateMemorySize64 / 1024.0 / 1024.0;

                        int inWorldCount = bots.Count(b => b.InWorld);
                        int target = config.BotCount;
                        string statusColor = inWorldCount >= target ? "green" : (inWorldCount > 0 ? "yellow" : "red");

                        int trackedMonstersTotal = bots.Sum(b => b.TrackedMonstersTotal);

                        int actions = metrics.Actions;
                        double actionsPerBot = inWorldCount > 0 ? (actions - lastActions) / sampleSeconds / inWorldCount : 0;
                        lastActions = actions;

                        DateTime activeThreshold = DateTime.UtcNow.AddSeconds(-2);
                        int activeAttackers = bots.Count(b => b.LastAttackTime > activeThreshold);
                        int takingDamage = bots.Count(b => b.LastDamageTakenTime > activeThreshold);
                        double pctAttacking = inWorldCount > 0 ? (activeAttackers * 100.0 / inWorldCount) : 0.0;
                        double pctDamage = inWorldCount > 0 ? (takingDamage * 100.0 / inWorldCount) : 0.0;

                        int failedCount = bots.Count(b => b.PermanentFailure);

                        var table = new Table().Border(TableBorder.Rounded).Expand();
                        table.AddColumn(new TableColumn("[bold teal]Metric[/]").Centered());
                        table.AddColumn(new TableColumn("[bold teal]Value[/]").Centered());
                        table.AddColumn(new TableColumn("[bold teal]Global Totals[/]").Centered());

                        table.AddRow(
                            "Status",
                            $"[{statusColor}]In-World: {inWorldCount} / {target}[/]",
                            $"[{statusColor}]Disc: {metrics.Disconnects} | Reconn: {metrics.Reconnects}[/]"
                        );
                        table.AddRow("Conexões", $"TCP: {metrics.ConnectedCount} | Falhas TCP: {metrics.ConnectionFailures}", $"Ping: {metrics.Pingbacks} | Turns: {metrics.Turns}");
                        if (metrics.LastError is string lastError)
                            table.AddRow("Último erro", Markup.Escape(lastError), "");
                        if (failedCount > 0)
                        {
                            var firstError = bots.FirstOrDefault(b => b.PermanentFailure)?.LastError ?? "?";
                            // Escape Spectre markup chars
                            firstError = firstError.Replace("[", "[[").Replace("]", "]]");
                            table.AddRow(
                                "[red]Errors[/]",
                                $"[red]Auth Failed: {failedCount} bots[/]",
                                $"[red]{Markup.Escape(firstError.Length > 40 ? firstError[..40] + "..." : firstError)}[/]"
                            );
                        }
                        table.AddRow(
                            "Network",
                            $"[blue]In:[/] {bytesInSec / sampleSeconds / 1024.0:F1} KB/s | [fuchsia]Out:[/] {bytesOutSec / sampleSeconds / 1024.0:F1} KB/s",
                            $"Pkt In: [blue]{metrics.PacketsIn}[/] | Out: [fuchsia]{metrics.Sent}[/]"
                        );
                        table.AddRow(
                            "Actions (/sec)",
                            $"[orange3]Actions/s/Bot:[/] {actionsPerBot:F2}",
                            $"Atk: {metrics.Attacks} | Wlk: {metrics.Walks} | Mgc: {metrics.Spells}"
                        );
                        table.AddRow(
                            "Engagement",
                            $"[maroon]Attacking:[/] {pctAttacking:F1}% ({activeAttackers})",
                            $"[red]Taking Dmg:[/] {pctDamage:F1}% ({takingDamage})"
                        );
                        table.AddRow(
                            "Telemetry",
                            $"[fuchsia]Avg Drain:[/] {metrics.AvgDrainMs:F2}ms | [fuchsia]Max Lag:[/] {metrics.MaxSendLagMs:F2}ms",
                            $"[silver]CPU:[/] {cpuUsage:F1}% | [silver]RAM:[/] {ramMb:F1} MB"
                        );
                        table.AddRow(
                            "Tracking",
                            $"[green]Monsters Seen:[/] {trackedMonstersTotal} (Global)",
                            $"[green]Avg Queue Wait:[/] {metrics.AvgQueueWaitMs:F2}ms"
                        );

                        var panel = new Panel(table)
                            .Header("[bold yellow]Tibia 8.60 StressBot Cluster[/]")
                            .Border(BoxBorder.Double);

                        ctx.UpdateTarget(panel);
                    }
                });
        }
    }
}
