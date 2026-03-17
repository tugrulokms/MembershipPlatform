using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MembershipService.Application.Subscriptions.Create;
using MembershipService.Domain.Entities;
using MembershipService.Domain.Enums;
using MembershipService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MembershipService.IntegrationTests;

public class SubscriptionApiTests : IClassFixture<MembershipApiFactory>
{
    private readonly HttpClient _client;
    private readonly MembershipApiFactory _factory;

    public SubscriptionApiTests(MembershipApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedPlanAsync(int planId = 1)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();

        if (!db.Plans.Any(p => p.Id == planId))
        {
            db.Plans.Add(new Plan
            {
                Id = planId,
                Name = "Pro",
                Price = 9.99m,
                Currency = Currency.USD,
                BillingPeriod = BillingPeriod.Monthly,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CreateSubscription_ReturnsCreated()
    {
        await SeedPlanAsync();
        var userId = Random.Shared.Next(10000, 99999);

        var response = await _client.PostAsJsonAsync("/subscriptions",
            new CreateSubscriptionCommand(userId, 1));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateSubscriptionResult>();
        result!.SubscriptionId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateSubscription_DuplicateUser_ReturnsConflict()
    {
        await SeedPlanAsync();
        var userId = Random.Shared.Next(10000, 99999);

        await _client.PostAsJsonAsync("/subscriptions",
            new CreateSubscriptionCommand(userId, 1));

        var response = await _client.PostAsJsonAsync("/subscriptions",
            new CreateSubscriptionCommand(userId, 1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateSubscription_InvalidUserId_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/subscriptions",
            new CreateSubscriptionCommand(0, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelSubscription_ExistingActive_ReturnsNoContent()
    {
        await SeedPlanAsync();
        var userId = Random.Shared.Next(10000, 99999);

        var createResponse = await _client.PostAsJsonAsync("/subscriptions",
            new CreateSubscriptionCommand(userId, 1));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateSubscriptionResult>();

        var cancelResponse = await _client.PostAsync(
            $"/subscriptions/{created!.SubscriptionId}/cancel", null);

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CancelSubscription_NonExistent_ReturnsConflict()
    {
        var response = await _client.PostAsync(
            $"/subscriptions/{Guid.NewGuid()}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetEntitlements_ReturnsOk()
    {
        var response = await _client.GetAsync("/users/1/entitlements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntitlements_InvalidUserId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/users/0/entitlements");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadinessCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
