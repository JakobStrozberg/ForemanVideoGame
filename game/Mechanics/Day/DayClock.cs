using System;

namespace Crewboss.Mechanics.Day;

/// <summary>One block day: the countdown, the pre-game overview, and the score.</summary>
public sealed class DayClock
{
    public const float DaySeconds = 480f;

    public float Remaining { get; private set; } = DaySeconds;
    public bool Over { get; private set; }
    /// <summary>Block overview before the day starts (any key to begin).</summary>
    public bool PreGame { get; set; } = true;

    /// <summary>Tick the clock. Returns true the frame the day ends.</summary>
    public bool Update(float dt)
    {
        if (Over) return false;
        Remaining -= dt;
        if (Remaining <= 0)
        {
            Remaining = 0;
            Over = true;
            return true;
        }
        return false;
    }

    public void Restart()
    {
        Remaining = DaySeconds;
        Over = false;
    }

    /// <summary>End-of-day rating, 0..3.</summary>
    public static int Stars(int trees, int faults, int idleSeconds)
    {
        int stars = 3;
        if (faults > trees * 0.10f) stars--;
        if (idleSeconds > 180) stars--;
        if (trees < 200) stars = Math.Min(stars, 1);
        return stars;
    }
}
