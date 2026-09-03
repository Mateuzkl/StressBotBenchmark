using StressBotBenchmark.World;

namespace StressBotBenchmark.Navigation;

/// <summary>
/// High-level navigation engine for a bot.
/// Provides chase, kite, wander, and obstacle avoidance.
/// </summary>
public sealed class NavigationEngine
{
    private readonly WorldState _world;
    private readonly LocalPathfinder _pathfinder;
    private readonly MovementPlanner _planner = new();
    private readonly FloorTransitionDetector _floorDetector;

    // Exploration memory: avoid oscillating N/S/N/S
    private (int Dx, int Dy) _lastDirection = (0, 0);
    private int _consecutiveStepsInDir = 0;

    public NavigationEngine(WorldState world)
    {
        _world = world;
        _pathfinder = new LocalPathfinder(world);
        _floorDetector = new FloorTransitionDetector(world.Player.Z);
    }

    public bool CanMove() => _planner.CanMove(_world.Player.Speed);

    public void OnMoveSent() => _planner.OnMoveSent();

    /// <summary>
    /// Find steps to chase a target creature up to desired distance (e.g. 1 for melee, 3-4 for distance).
    /// </summary>
    public List<(int Dx, int Dy)> PlanChase(CreatureState target, int desiredDistance = 1)
    {
        var player = _world.Player;
        if (target.Z != player.Z) return new List<(int Dx, int Dy)>();

        int currentDist = target.ChebyshevDistanceTo(player.X, player.Y);
        if (currentDist <= desiredDistance)
            return new List<(int Dx, int Dy)>(); // Already close enough!

        var path = _pathfinder.FindPath(player.X, player.Y, target.X, target.Y, player.Z, maxSteps: 6);
        // If path found and ends at target tile, trim last steps if desiredDistance > 0
        if (path.Count > 0 && desiredDistance > 0 && path.Count >= desiredDistance)
        {
            // Remove steps that would bring closer than desiredDistance
            while (path.Count > 1 && path.Count > (currentDist - desiredDistance))
            {
                path.RemoveAt(path.Count - 1);
            }
        }

        return path;
    }

    /// <summary>
    /// Find steps to kite (move away from) a target if too close.
    /// </summary>
    public (int Dx, int Dy)? PlanKite(CreatureState target, int safeDistance = 3)
    {
        var player = _world.Player;
        if (target.Z != player.Z) return null;

        int currentDist = target.ChebyshevDistanceTo(player.X, player.Y);
        if (currentDist >= safeDistance)
            return null; // Already at safe distance

        // Vector away from target
        int awayDx = Math.Sign(player.X - target.X);
        int awayDy = Math.Sign(player.Y - target.Y);

        if (awayDx == 0 && awayDy == 0)
            awayDx = 1; // Arbitrary fallback

        // Candidate directions: direct away, diagonal away, perpendicular
        (int Dx, int Dy)[] candidates =
        {
            (awayDx, awayDy),
            (awayDx, 0),
            (0, awayDy),
            (awayDx, -awayDy),
            (-awayDx, awayDy)
        };

        foreach (var (dx, dy) in candidates)
        {
            if (dx == 0 && dy == 0) continue;
            ushort nx = (ushort)(player.X + dx);
            ushort ny = (ushort)(player.Y + dy);
            if (_pathfinder.IsWalkable(nx, ny, player.Z))
            {
                return (dx, dy);
            }
        }

        return null;
    }

    /// <summary>
    /// Natural wander / explore step:
    /// Chooses walkable adjacent tiles, preferring forward continuation
    /// over immediate 180-degree turnarounds (prevents N/S oscillation).
    /// </summary>
    public (int Dx, int Dy)? PlanWander(Random rng)
    {
        var player = _world.Player;
        var validMoves = new List<(int Dx, int Dy)>();

        // 8 directions
        (int Dx, int Dy)[] allDirs =
        {
            (0, -1), (1, -1), (1, 0), (1, 1),
            (0, 1), (-1, 1), (-1, 0), (-1, -1)
        };

        foreach (var (dx, dy) in allDirs)
        {
            ushort nx = (ushort)(player.X + dx);
            ushort ny = (ushort)(player.Y + dy);
            if (_pathfinder.IsWalkable(nx, ny, player.Z))
            {
                validMoves.Add((dx, dy));
            }
        }

        if (validMoves.Count == 0)
            return null; // Boxed in

        // If we have a current direction and it's still walkable and haven't exceeded 6 steps in same dir:
        if (_consecutiveStepsInDir < 5 && validMoves.Contains(_lastDirection) && rng.NextDouble() < 0.70)
        {
            _consecutiveStepsInDir++;
            return _lastDirection;
        }

        // Avoid exact reverse if possible
        var reverse = (-_lastDirection.Dx, -_lastDirection.Dy);
        var nonReverseMoves = validMoves.Where(m => m != reverse).ToList();
        var choices = nonReverseMoves.Count > 0 ? nonReverseMoves : validMoves;

        var chosen = choices[rng.Next(choices.Count)];
        _lastDirection = chosen;
        _consecutiveStepsInDir = 1;
        return chosen;
    }
}
