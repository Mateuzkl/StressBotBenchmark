using StressBotBenchmark.World;

namespace StressBotBenchmark.AI.Behaviors;

/// <summary>
/// Handles real target acquisition and maintenance according to TFS rules:
/// - Only acquires Monsters (Type == CreatureType.Monster)
/// - Monster must be visible, HP > 0, on same floor (Z == player.Z)
/// - NEVER targets Players or NPCs!
/// - Keeps target until dead, invisible, out of range, or floor changed.
/// </summary>
public sealed class TargetBehavior
{
    private uint _currentTargetId;
    private DateTime _targetAcquiredTime = DateTime.MinValue;

    public uint CurrentTargetId => _currentTargetId;

    public CreatureState? GetCurrentTarget(WorldState world)
    {
        if (_currentTargetId == 0) return null;
        var creature = world.GetCreature(_currentTargetId);
        if (IsValidTarget(creature, world.Player))
            return creature;

        // Target no longer valid, clear it
        _currentTargetId = 0;
        world.Player.CurrentTargetId = 0;
        return null;
    }

    public bool IsValidTarget(CreatureState? target, PlayerState player)
    {
        if (target == null) return false;
        return target.Visible &&
               target.HealthPercent > 0 &&
               target.Z == player.Z &&
               target.Type == CreatureType.Monster; // Strict: MONSTERS ONLY
    }

    /// <summary>
    /// Update or acquire a valid target.
    /// Returns the active target creature, or null if no valid monsters are around.
    /// </summary>
    public CreatureState? UpdateTarget(WorldState world)
    {
        var active = GetCurrentTarget(world);
        if (active != null)
            return active;

        // Acquire new target: closest visible monster on same floor with HP > 0
        CreatureState? bestMonster = null;
        int bestDist = int.MaxValue;
        var p = world.Player;

        foreach (var monster in world.GetVisibleMonsters())
        {
            if (monster.Z != p.Z || monster.HealthPercent == 0 || !monster.Visible)
                continue;

            int dist = monster.ChebyshevDistanceTo(p.X, p.Y);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestMonster = monster;
            }
        }

        if (bestMonster != null)
        {
            _currentTargetId = bestMonster.Id;
            world.Player.CurrentTargetId = bestMonster.Id;
            _targetAcquiredTime = DateTime.UtcNow;
            return bestMonster;
        }

        _currentTargetId = 0;
        world.Player.CurrentTargetId = 0;
        return null;
    }

    public void ClearTarget(WorldState world)
    {
        _currentTargetId = 0;
        world.Player.CurrentTargetId = 0;
    }
}
