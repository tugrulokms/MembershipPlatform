using FluentAssertions;
using MembershipService.Application.Interfaces;
using MembershipService.Application.Subscriptions.Create;
using MembershipService.Domain.Entities;
using MembershipService.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MembershipService.UnitTests.Application;

public class CreateSubscriptionHandlerTests
{
    private readonly IPlanRepository _planRepo = Substitute.For<IPlanRepository>();
    private readonly ISubscriptionRepository _subRepo = Substitute.For<ISubscriptionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly CreateSubscriptionHandler _handler;

    public CreateSubscriptionHandlerTests()
    {
        _handler = new CreateSubscriptionHandler(_planRepo, _subRepo, _uow);
    }

    private static Plan ActivePlan() => new()
    {
        Id = 1, Name = "Pro", Price = 9.99m, IsActive = true,
        CreatedAt = DateTime.UtcNow, BillingPeriod = BillingPeriod.Monthly
    };

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesSubscription()
    {
        var plan = ActivePlan();
        _planRepo.GetByIdAsync(1).Returns(plan);
        _subRepo.GetActiveByUserIdAsync(1).Returns((Subscription?)null);

        var result = await _handler.HandleAsync(new CreateSubscriptionCommand(1, 1));

        result.SubscriptionId.Should().NotBeEmpty();
        await _subRepo.Received(1).AddAsync(Arg.Any<Subscription>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlanNotFound_Throws()
    {
        _planRepo.GetByIdAsync(99).Returns((Plan?)null);

        var act = () => _handler.HandleAsync(new CreateSubscriptionCommand(1, 99));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task HandleAsync_PlanInactive_Throws()
    {
        var plan = ActivePlan();
        plan.IsActive = false;
        _planRepo.GetByIdAsync(1).Returns(plan);

        var act = () => _handler.HandleAsync(new CreateSubscriptionCommand(1, 1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no longer available*");
    }

    [Fact]
    public async Task HandleAsync_UserAlreadyHasActiveSubscription_Throws()
    {
        _planRepo.GetByIdAsync(1).Returns(ActivePlan());
        _subRepo.GetActiveByUserIdAsync(1).Returns(Subscription.Create(1, 1, ActivePlan()));

        var act = () => _handler.HandleAsync(new CreateSubscriptionCommand(1, 1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has an active subscription*");
    }
}
