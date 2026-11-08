namespace AprsLive.Core.Links;

/// <summary>Wait before each automatic reconnection attempt: 10 s, 30 s, 1 min, then every 2 min.</summary>
public static class ReconnectSchedule
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
    ];

    /// <param name="attempt">1 for the first attempt after the drop.</param>
    public static TimeSpan DelayBefore(int attempt) => Steps[Math.Clamp(attempt, 1, Steps.Length) - 1];
}
