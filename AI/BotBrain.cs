using StressBotBenchmark.AI.Behaviors;
using StressBotBenchmark.Navigation;
using StressBotBenchmark.Network;
using StressBotBenchmark.Protocol;
using StressBotBenchmark.World;

namespace StressBotBenchmark.AI;

/// <summary>
/// Unified brain evaluating per-bot decisions on a periodic tick (~150-250ms).
/// Replaces the independent conflicting loops with a single priority-driven state machine:
/// 1. Healing
/// 2. Potion
/// 3. Flee / Reposition
/// 4. Acquire / Maintain Target
/// 5. Combat Action (Attack / Spells — ONLY with valid target)
/// 6. Movement (Chase, Kite, or Wander)
/// 7. Chat / Cosmetics
/// </summary>
public sealed class BotBrain
{
    private readonly WorldState _world;
    private readonly BotPersona _persona;
    private readonly BotConfig _config;
    private readonly CooldownManager _cooldowns = new();
    private readonly NavigationEngine _nav;

    // Behaviors
    private readonly HealBehavior _heal = new();
    private readonly PotionBehavior _potion = new();
    private readonly TargetBehavior _target = new();
    private readonly CombatBehavior _combat = new();
    private readonly ChatBehavior _chat = new();
    private readonly OutfitBehavior _outfit = new();

    private bool _fightModesSent = false;

    public BotBrain(WorldState world, BotConfig config, int seed, VocationProfile? vocation = null)
    {
        _world = world;
        _config = config;
        _persona = new BotPersona(seed, vocation ?? config.VocationConfig);
        _nav = new NavigationEngine(world);
    }

    public BotPersona Persona => _persona;
    public WorldState World => _world;
    public NavigationEngine Navigation => _nav;
    public TargetBehavior Target => _target;

    /// <summary>
    /// Executes one decision tick. Returns the chosen OutputMessage action to send to the server, or null.
    /// </summary>
    public OutputMessage? Tick()
    {
        var ctx = new DecisionContext(_world, _persona, _cooldowns, _config);

        // 0. Initial Fight Modes sync
        if (!_fightModesSent)
        {
            _fightModesSent = true;
            return Protocol860Writer.FightModes(
                _config.FightMode,
                (byte)(_config.EnableChaseMode ? 1 : 0),
                (byte)(_config.SafeFight ? 1 : 0));
        }

        // 1. Healing
        var healAction = _heal.Evaluate(ctx);
        if (healAction != null)
            return healAction;

        // 2. Potion
        var potionAction = _potion.Evaluate(ctx);
        if (potionAction != null)
            return potionAction;

        // 3. Target acquisition (STRICT: Monsters only)
        var targetCreature = _config.EnableAttack ? _target.UpdateTarget(_world) : null;

        // 4. Combat action (attack packet or offensive spell)
        // STRICT RULE: Offensive spells and attacks ONLY trigger when targetCreature is valid!
        if (targetCreature != null)
        {
            var combatAction = _combat.Evaluate(ctx, targetCreature);
            if (combatAction != null)
                return combatAction;
        }

        // 5. Movement (Chase, Kite, or Explore)
        if (_nav.CanMove())
        {
            // If in combat with a valid monster:
            if (targetCreature != null)
            {
                int dist = targetCreature.ChebyshevDistanceTo(_world.Player.X, _world.Player.Y);

                // If mage/paladin and too close -> Kite!
                if (_persona.PreferredRange > 1 && dist < _persona.PreferredRange)
                {
                    var kiteStep = _nav.PlanKite(targetCreature, _persona.PreferredRange);
                    if (kiteStep.HasValue)
                    {
                        _nav.OnMoveSent();
                        byte opcode = Protocol860Writer.DirectionToOpcode(kiteStep.Value.Dx, kiteStep.Value.Dy);
                        return Protocol860Writer.MoveStep(opcode);
                    }
                }
                // If too far from preferred range -> Chase!
                else if (dist > _persona.PreferredRange && _config.EnableChaseMode)
                {
                    var chaseSteps = _nav.PlanChase(targetCreature, _persona.PreferredRange);
                    if (chaseSteps.Count > 0)
                    {
                        _nav.OnMoveSent();
                        if (chaseSteps.Count > 1)
                        {
                            return Protocol860Writer.AutoWalk(chaseSteps);
                        }
                        else
                        {
                            byte opcode = Protocol860Writer.DirectionToOpcode(chaseSteps[0].Dx, chaseSteps[0].Dy);
                            return Protocol860Writer.MoveStep(opcode);
                        }
                    }
                }
            }
            // No target or not attacking: natural wander/explore if enabled
            else if (_config.EnableRandomWalk)
            {
                var step = _nav.PlanWander(_persona.Rng);
                if (step.HasValue)
                {
                    _nav.OnMoveSent();
                    byte opcode = Protocol860Writer.DirectionToOpcode(step.Value.Dx, step.Value.Dy);
                    return Protocol860Writer.MoveStep(opcode);
                }
            }
        }

        // 6. Rare humanized Chat
        var chatAction = _chat.Evaluate(ctx);
        if (chatAction != null)
            return chatAction;

        // 7. Rare Outfit cosmetic
        var outfitAction = _outfit.Evaluate(ctx);
        if (outfitAction != null)
            return outfitAction;

        return null;
    }
}
