using System.Text.Json;
using MembershipService.Application.Interfaces;
using MembershipService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MembershipService.Infrastructure.Persistence;

public class MembershipDbContext : DbContext, IUnitOfWork
{
    private readonly ICorrelationIdProvider _correlationIdProvider;
    public MembershipDbContext(DbContextOptions<MembershipDbContext> options, ICorrelationIdProvider correlationIdProvider)
        : base(options)
    {
        _correlationIdProvider = correlationIdProvider;
    }

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<DlqMessage> DlqMessages => Set<DlqMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MembershipDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ConvertDomainEventsToOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
    }


    private void ConvertDomainEventsToOutboxMessages()
    {
        // Find all tracked entities that inherit from BaseEntity and have domain events
        var entities = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = domainEvent.GetType().Name,
                    CorrelationId = _correlationIdProvider.Get(),
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    CreatedAt = DateTime.UtcNow
                };

                OutboxMessages.Add(outboxMessage);
            }

            entity.ClearDomainEvents();
        }
    }
}
