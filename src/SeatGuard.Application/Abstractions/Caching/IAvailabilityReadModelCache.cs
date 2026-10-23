using SeatGuard.Domain.Events;

namespace SeatGuard.Application.Abstractions.Caching;

/// <summary>
/// Short-lived cache for the availability read model. Invalidation is best-effort:
/// the TTL bounds staleness when another replica serves the write.
/// </summary>
public interface IAvailabilityReadModelCache
{
    bool TryGet<T>(string key, out T? value) where T : class;

    void Store<T>(string key, T value, TimeSpan timeToLive) where T : class;

    void Remove(string key);
}

public static class AvailabilityCacheKeys
{
    public static string ForEvent(EventId eventId) => $"availability:{eventId}";
}
