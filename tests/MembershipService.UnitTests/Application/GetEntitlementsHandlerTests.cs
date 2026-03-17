using FluentAssertions;
using MembershipService.Application.DTOs;
using MembershipService.Application.Entitlements.Get;
using MembershipService.Application.Interfaces;
using MembershipService.Domain.Entities;
using MembershipService.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MembershipService.UnitTests.Application;

public class GetEntitlementsHandlerTests
{
    private readonly IEntitlementRepository _repo = Substitute.For<IEntitlementRepository>();
    private readonly IEntitlementCacheService _cache = Substitute.For<IEntitlementCacheService>();
    private readonly GetEntitlementsHandler _handler;

    public GetEntitlementsHandlerTests()
    {
        _handler = new GetEntitlementsHandler(_repo, _cache);
    }

    [Fact]
    public async Task HandleAsync_CacheHit_ReturnsCachedData_WithoutHittingDb()
    {
        var cached = new List<EntitlementDto>
        {
            new() { FeatureKey = "premium", IsEnabled = true, UserId = 1, SubscriptionId = Guid.NewGuid() }
        };
        _cache.GetEntitlementsByUserAsync(1).Returns(cached);

        var result = await _handler.HandleAsync(1);

        result.Should().BeSameAs(cached);
        await _repo.DidNotReceive().GetByUserIdAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task HandleAsync_CacheMiss_LoadsFromDb_AndCachesResult()
    {
        _cache.GetEntitlementsByUserAsync(1).Returns((IReadOnlyList<EntitlementDto>?)null);
        var entitlements = new List<Entitlement>
        {
            new()
            {
                Id = 1, UserId = 1, SubscriptionId = Guid.NewGuid(),
                FeatureKey = "premium", Type = EntitlementType.FeatureFlag, IsEnabled = true
            }
        };
        _repo.GetByUserIdAsync(1).Returns(entitlements);

        var result = await _handler.HandleAsync(1);

        result.Should().HaveCount(1);
        result[0].FeatureKey.Should().Be("premium");
        await _cache.Received(1).SetEntitlementsAsync(1, Arg.Any<IReadOnlyList<EntitlementDto>>());
    }

    [Fact]
    public async Task HandleAsync_CacheMiss_EmptyResult_DoesNotCache()
    {
        _cache.GetEntitlementsByUserAsync(1).Returns((IReadOnlyList<EntitlementDto>?)null);
        _repo.GetByUserIdAsync(1).Returns(new List<Entitlement>());

        var result = await _handler.HandleAsync(1);

        result.Should().BeEmpty();
        await _cache.DidNotReceive().SetEntitlementsAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<EntitlementDto>>());
    }
}
