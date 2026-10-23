using Microsoft.Extensions.Caching.Memory;
using SeatGuard.Application.Abstractions.Caching;

namespace SeatGuard.Infrastructure.Caching;

internal sealed class MemoryAvailabilityReadModelCache(IMemoryCache cache) : IAvailabilityReadModelCache
{
    public bool TryGet<T>(string key, out T? value) where T : class
        => cache.TryGetValue(key, out value);

    public void Store<T>(string key, T value, TimeSpan timeToLive) where T : class
        => cache.Set(key, value, timeToLive);

    public void Remove(string key)
        => cache.Remove(key);
}
