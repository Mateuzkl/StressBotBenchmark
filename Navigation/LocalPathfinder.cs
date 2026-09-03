using StressBotBenchmark.Data;
using StressBotBenchmark.World;

namespace StressBotBenchmark.Navigation;

/// <summary>
/// Fast, lightweight local pathfinder (BFS) for headless bots.
/// Operates on the local visible tile cache (within ~9x7 radius).
/// Max nodes bounded to ~100 to support 1000 bots with minimal CPU/RAM.
/// </summary>
public sealed class LocalPathfinder
{
    private readonly WorldState _world;

    // 8-directional offsets: East, Northeast, North, Northwest, West, Southwest, South, Southeast
    private static readonly (int Dx, int Dy)[] Directions =
    {
        (1, 0), (1, -1), (0, -1), (-1, -1),
        (-1, 0), (-1, 1), (0, 1), (1, 1)
    };

    public LocalPathfinder(WorldState world)
    {
        _world = world;
    }

    /// <summary>
    /// Find a path from (startX, startY) to (targetX, targetY) on floor Z.
    /// Returns a list of relative steps (dx, dy) or empty if unreachable.
    /// </summary>
    public List<(int Dx, int Dy)> FindPath(ushort startX, ushort startY, ushort targetX, ushort targetY, byte z, int maxSteps = 10)
    {
        if (startX == targetX && startY == targetY)
            return new List<(int Dx, int Dy)>();

        var queue = new Queue<(ushort X, ushort Y)>();
        var parent = new Dictionary<(ushort, ushort), ((ushort X, ushort Y) Parent, int Dx, int Dy)>();
        var visited = new HashSet<(ushort, ushort)>();

        var start = (startX, startY);
        queue.Enqueue(start);
        visited.Add(start);

        int nodesEvaluated = 0;
        const int maxNodes = 120; // Bounded for high scale

        bool found = false;

        while (queue.Count > 0 && nodesEvaluated < maxNodes)
        {
            var current = queue.Dequeue();
            nodesEvaluated++;

            if (current.X == targetX && current.Y == targetY)
            {
                found = true;
                break;
            }

            foreach (var (dx, dy) in Directions)
            {
                ushort nextX = (ushort)(current.X + dx);
                ushort nextY = (ushort)(current.Y + dy);
                var nextPos = (nextX, nextY);

                if (visited.Contains(nextPos))
                    continue;

                // If nextPos is destination, it's ok if the target creature is standing there
                bool isDestination = (nextX == targetX && nextY == targetY);

                if (!IsWalkable(nextX, nextY, z, isDestination))
                    continue;

                visited.Add(nextPos);
                parent[nextPos] = (current, dx, dy);
                queue.Enqueue(nextPos);
            }
        }

        if (!found)
            return new List<(int Dx, int Dy)>();

        // Reconstruct path
        var path = new List<(int Dx, int Dy)>();
        var curr = (targetX, targetY);

        while (curr != start && parent.TryGetValue(curr, out var info))
        {
            path.Add((info.Dx, info.Dy));
            curr = info.Parent;
            if (path.Count >= maxSteps)
                break;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Check if tile (x, y, z) is walkable.
    /// </summary>
    public bool IsWalkable(ushort x, ushort y, byte z, bool allowDestinationCreature = false)
    {
        var tile = _world.GetTile(x, y, z);
        if (tile == null)
            return false; // Unknown tile

        if (!tile.HasGround)
            return false; // Void/unwalkable

        // Check if ground is blocking
        if (ItemMetadata.IsBlocking(tile.GroundId))
            return false;

        // Check items on tile
        foreach (var itemId in tile.ItemIds)
        {
            if (ItemMetadata.IsBlocking(itemId))
                return false;
        }

        // Check creatures on tile
        if (!allowDestinationCreature && tile.HasCreature)
        {
            // If any non-walkthrough creature is there, tile is blocked
            foreach (var cid in tile.CreatureIds)
            {
                if (cid == _world.Player.Id) continue;
                var c = _world.GetCreature(cid);
                if (c != null && !c.Walkthrough)
                    return false;
            }
        }

        return true;
    }
}
