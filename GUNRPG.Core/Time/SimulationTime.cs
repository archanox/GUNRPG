namespace GUNRPG.Core.Time;

/// <summary>
/// Represents the global simulation clock.
/// All time in the simulation is measured in milliseconds.
/// </summary>
public class SimulationTime
{
    /// <summary>
    /// Current time in milliseconds.
    /// </summary>
    public long CurrentTimeMs { get; private set; }

    /// <summary>
    /// Advances the simulation time by the specified amount.
    /// </summary>
    /// <param name="deltaMs">Amount of time to advance in milliseconds.</param>
    public void Advance(long deltaMs)
    {
        if (deltaMs < 0)
            throw new ArgumentException("Cannot advance time backwards", nameof(deltaMs));
        
        CurrentTimeMs += deltaMs;
    }

    /// <summary>
    /// Resets the simulation time to zero.
    /// </summary>
    public void Reset()
    {
        CurrentTimeMs = 0;
    }
}
