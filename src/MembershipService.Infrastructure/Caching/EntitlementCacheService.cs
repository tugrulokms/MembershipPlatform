using System.Text.Json;
using MembershipService.Application.DTOs;
using MembershipService.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace MembershipService.Infrastructure.Caching;

public class EntitlementCacheService : IEntitlementCacheService
{

    private readonly IDistributedCache _cache;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);

    public EntitlementCacheService(IDistributedCache cache)
    {
        _cache = cache;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
    private static string CacheKey(int userId) => $"membership:entitlements:user:{userId}";

    public async Task<IReadOnlyList<EntitlementDto>?> GetEntitlementsByUserAsync(int userId)
    {
        string key = CacheKey(userId);
        var json = await _cache.GetStringAsync(key);

        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<IReadOnlyList<EntitlementDto>>(json, _jsonOptions);
    }

    public async Task InvalidateAsync(int userId)
    {
        await _cache.RemoveAsync(CacheKey(userId));
    }

    public async Task SetEntitlementsAsync(int userId, IReadOnlyList<EntitlementDto> entitlements)
    {
        string key = CacheKey(userId);
        var json = JsonSerializer.Serialize(entitlements, _jsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl
        };

        await _cache.SetStringAsync(key, json, options);
    }
}