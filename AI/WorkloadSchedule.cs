namespace StressBotBenchmark.AI;

public enum ActivityState { Idle, Walking, Combat, Social }

public sealed class ActivityWeights
{
    public double Idle { get; set; } = 35;
    public double Walking { get; set; } = 30;
    public double Combat { get; set; } = 28;
    public double Social { get; set; } = 7;
}

public static class StableSeed
{
    public static int For(string identity, int seed, int stream = 0)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in identity) hash = (hash ^ character) * 16777619;
            return (int)(hash ^ (uint)seed ^ ((uint)stream * 0x9e3779b9));
        }
    }
}

// Seeded semi-Markov schedule: weights select states; durations control dwell
// time. They are not a promise of exact simultaneous population percentages.
public sealed class WorkloadSchedule
{
    private readonly Random random;
    private readonly ActivityWeights weights;
    private long deadline;
    public ActivityState State { get; private set; }

    public WorkloadSchedule(ActivityWeights weights, int seed)
    {
        double[] values = { weights.Idle, weights.Walking, weights.Combat, weights.Social };
        if (values.Any(w => !double.IsFinite(w) || w < 0) || !double.IsFinite(values.Sum()) || values.Sum() <= 0)
            throw new ArgumentException("Activity weights must be finite, nonnegative and have a positive sum.");
        this.weights = weights;
        random = new Random(seed);
        State = Choose();
        deadline = Duration(State);
    }

    public bool Advance(long elapsedMilliseconds)
    {
        if (elapsedMilliseconds < deadline) return false;
        ActivityState previous = State;
        State = previous == ActivityState.Combat ? ActivityState.Idle : Choose();
        deadline = elapsedMilliseconds + (previous == ActivityState.Combat
            ? random.Next(2000, 15001) : Duration(State));
        return State != previous;
    }

    private ActivityState Choose()
    {
        double choice = random.NextDouble() * (weights.Idle + weights.Walking + weights.Combat + weights.Social);
        if ((choice -= weights.Idle) < 0) return ActivityState.Idle;
        if ((choice -= weights.Walking) < 0) return ActivityState.Walking;
        return (choice -= weights.Combat) < 0 ? ActivityState.Combat : ActivityState.Social;
    }

    private int Duration(ActivityState state) => state switch
    {
        ActivityState.Idle => random.Next(5000, 60001),
        ActivityState.Walking => random.Next(3000, 30001),
        ActivityState.Combat => random.Next(5000, 45001),
        _ => random.Next(5000, 15001)
    };
}
