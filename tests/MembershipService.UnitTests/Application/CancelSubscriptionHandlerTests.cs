using FluentAssertions;
using MembershipService.Application.Interfaces;
using MembershipService.Application.Subscriptions.Cancel;
using MembershipService.Domain.Entities;
using MembershipService.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MembershipService.UnitTests.Application;

public class CancelSubscriptionHandlerTests
{
    private readonly ISubscriptionRepository _subRepo = Substitute.For<ISubscriptionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEntitlementCacheService _cache = Substitute.For<IEntitlementCacheService>();
    private readonly ILogger<CancelSubscriptionHandler> _logger = Substitute.For<ILogger<CancelSubscriptionHandler>>();
    private readonly CancelSubscriptionHandler _handler;

    public CancelSubscriptionHandlerTests()
    {
        _handler = new CancelSubscriptionHandler(_cache, _subRepo, _uow, _logger);
    }

    private static Plan DefaultPlan() => new()
    {
        Id = 1, Name = "Pro", Price = 9.99m, IsActive = true,
        CreatedAt = DateTime.UtcNow, BillingPeriod = BillingPeriod.Monthly
    };

    [Fact]
    public async Task HandleAsync_ActiveSubscription_CancelsAndInvalidatesCache()
    {
        var sub = Subscription.Create(1, 1, DefaultPlan());
        _subRepo.GetByIdAsync(sub.Id).Returns(sub);

        await _handler.HandleAsync(new CancelSubscriptionCommand(sub.Id));

        sub.Status.Should().Be(SubscriptionStatus.Canceled);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cache.Received(1).InvalidateAsync(1);
    }

    [Fact]
    public async Task HandleAsync_SubscriptionNotFound_Throws()
    {
        var id = Guid.NewGuid();
        _subRepo.GetByIdAsync(id).Returns((Subscription?)null);

        var act = () => _handler.HandleAsync(new CancelSubscriptionCommand(id));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task HandleAsync_CacheFailure_DoesNotThrow()
    {
        var sub = Subscription.Create(1, 1, DefaultPlan());
        _subRepo.GetByIdAsync(sub.Id).Returns(sub);
        _cache.InvalidateAsync(1).ThrowsAsync(new TimeoutException("Redis down"));

        var act = () => _handler.HandleAsync(new CancelSubscriptionCommand(sub.Id));

        await act.Should().NotThrowAsync();
        sub.Status.Should().Be(SubscriptionStatus.Canceled);
    }
}
