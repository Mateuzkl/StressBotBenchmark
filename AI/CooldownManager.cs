namespace StressBotBenchmark.AI;

/// <summary>
/// Manages independent cooldowns for spells, items, actions, and movement.
/// </summary>
public sealed class CooldownManager
{
    private readonly Dictionary<string, DateTime> _cooldowns = new();

    /// <summary>
    /// Check if a named cooldown has expired.
    /// </summary>
    public bool IsReady(string key)
    {
        if (!_cooldowns.TryGetValue(key, out var expireTime))
            return true;

        return DateTime.UtcNow >= expireTime;
    }

    /// <summary>
    /// Set a named cooldown for a specified millisecond duration.
    /// </summary>
    public void SetCooldown(string key, int durationMs)
    {
        _cooldowns[key] = DateTime.UtcNow.AddMilliseconds(durationMs);
    }

    /// <summary>
    /// Global exhaust for offensive spells / actions (typically 1000-2000ms).
    /// </summary>
    public bool IsActionReady() => IsReady("global_action");

    public void TriggerGlobalActionCooldown(int durationMs = 1000)
    {
        SetCooldown("global_action", durationMs);
    }
}
