namespace SeatGuard.Application.Common;

/// <summary>
/// Outbox dispatcher settings configured under the "Outbox" section.
/// </summary>
public sealed class OutboxSettings
{
    public const string SectionName = "Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How long a claimed row is invisible to other dispatchers. If the process
    /// dies mid-publish, the lease expires and the row is redelivered.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public bool Enabled { get; set; } = true;
}
