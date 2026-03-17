using FluentAssertions;
using MembershipService.Domain.Entities;
using MembershipService.Domain.Enums;
using MembershipService.Domain.Events;
using Xunit;

namespace MembershipService.UnitTests.Domain;

public class SubscriptionTests
{
    private static Plan CreatePlan(BillingPeriod period = BillingPeriod.Monthly) => new()
    {
        Name = "Pro",
        Price = 9.99m,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        BillingPeriod = period,
        Currency = Currency.USD
    };

    [Fact]
    public void Create_SetsActiveStatus_And_RaisesCreatedEvent()
    {
        var plan = CreatePlan();

        var sub = Subscription.Create(userId: 1, planId: 1, plan);

        sub.Status.Should().Be(SubscriptionStatus.Active);
        sub.UserId.Should().Be(1);
        sub.PlanId.Should().Be(1);
        sub.Id.Should().NotBeEmpty();
        sub.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SubscriptionCreatedEvent>();
    }

    [Theory]
    [InlineData(BillingPeriod.Monthly, 1)]
    [InlineData(BillingPeriod.Quarterly, 3)]
    [InlineData(BillingPeriod.Yearly, 12)]
    public void Create_SetsCorrectPeriodEnd_BasedOnBillingPeriod(BillingPeriod period, int expectedMonths)
    {
        var plan = CreatePlan(period);

        var sub = Subscription.Create(1, 1, plan);

        var expectedEnd = sub.CurrentPeriodStart.AddMonths(expectedMonths);
        sub.CurrentPeriodEnd.Should().BeCloseTo(expectedEnd, TimeSpan.FromSeconds(1));
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_FromActive_TransitionsToCanceled_And_RaisesEvent()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.ClearDomainEvents();

        sub.Cancel();

        sub.Status.Should().Be(SubscriptionStatus.Canceled);
        sub.CanceledAt.Should().NotBeNull();
        sub.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SubscriptionCanceledEvent>();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Expired)]
    public void Cancel_FromNonActive_Throws(SubscriptionStatus initialStatus)
    {
        var sub = CreateSubscriptionInStatus(initialStatus);

        sub.Invoking(s => s.Cancel())
            .Should().Throw<InvalidOperationException>();
    }

    // --- MarkPastDue ---

    [Fact]
    public void MarkPastDue_FromActive_TransitionsToPastDue()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());

        sub.MarkPastDue();

        sub.Status.Should().Be(SubscriptionStatus.PastDue);
        sub.PastDueAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Expired)]
    public void MarkPastDue_FromNonActive_Throws(SubscriptionStatus initialStatus)
    {
        var sub = CreateSubscriptionInStatus(initialStatus);

        sub.Invoking(s => s.MarkPastDue())
            .Should().Throw<InvalidOperationException>();
    }

    // --- Activate ---

    [Fact]
    public void Activate_FromPastDue_TransitionsToActive()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.MarkPastDue();

        sub.Activate();

        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Activate_FromCanceled_TransitionsToActive_And_ClearsCanceledAt()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.Cancel();

        sub.Activate();

        sub.Status.Should().Be(SubscriptionStatus.Active);
        sub.CanceledAt.Should().BeNull();
    }

    [Fact]
    public void Activate_FromActive_Throws()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());

        sub.Invoking(s => s.Activate())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Activate_FromExpired_Throws()
    {
        var sub = CreateSubscriptionInStatus(SubscriptionStatus.Expired);

        sub.Invoking(s => s.Activate())
            .Should().Throw<InvalidOperationException>();
    }

    // --- Expire ---

    [Fact]
    public void Expire_FromPastDue_TransitionsToExpired_And_RaisesEvent()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.MarkPastDue();
        sub.ClearDomainEvents();

        sub.Expire();

        sub.Status.Should().Be(SubscriptionStatus.Expired);
        sub.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SubscriptionExpiredEvent>();
    }

    [Fact]
    public void Expire_FromCanceled_TransitionsToExpired()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.Cancel();
        sub.ClearDomainEvents();

        sub.Expire();

        sub.Status.Should().Be(SubscriptionStatus.Expired);
    }

    [Fact]
    public void Expire_FromActive_Throws()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());

        sub.Invoking(s => s.Expire())
            .Should().Throw<InvalidOperationException>();
    }

    // --- IsStaleEvent ---

    [Fact]
    public void IsStaleEvent_ReturnsFalse_WhenNoLastTimestamp()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());

        sub.IsStaleEvent(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsStaleEvent_ReturnsFalse_WhenEventIsNewer()
    {
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.LastBillingEventTimestamp = DateTime.UtcNow.AddMinutes(-5);

        sub.IsStaleEvent(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsStaleEvent_ReturnsTrue_WhenEventIsOlderOrEqual()
    {
        var lastTimestamp = DateTime.UtcNow;
        var sub = Subscription.Create(1, 1, CreatePlan());
        sub.LastBillingEventTimestamp = lastTimestamp;

        sub.IsStaleEvent(lastTimestamp).Should().BeTrue();
        sub.IsStaleEvent(lastTimestamp.AddMinutes(-1)).Should().BeTrue();
    }

    // --- Helper ---

    private static Subscription CreateSubscriptionInStatus(SubscriptionStatus targetStatus)
    {
        var sub = Subscription.Create(1, 1, CreatePlan());

        switch (targetStatus)
        {
            case SubscriptionStatus.Active:
                break;
            case SubscriptionStatus.Canceled:
                sub.Cancel();
                break;
            case SubscriptionStatus.PastDue:
                sub.MarkPastDue();
                break;
            case SubscriptionStatus.Expired:
                sub.MarkPastDue();
                sub.Expire();
                break;
        }

        sub.ClearDomainEvents();
        return sub;
    }
}
