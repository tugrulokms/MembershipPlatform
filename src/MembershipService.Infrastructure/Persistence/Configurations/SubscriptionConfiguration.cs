using MembershipService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MembershipService.Infrastructure.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.CurrentPeriodStart).IsRequired();
        builder.Property(s => s.CurrentPeriodEnd).IsRequired();

        // Navigation: a Subscription belongs to one Plan.
        // We configure the FK explicitly so EF Core uses PlanId,
        // not creating a shadow property.
        builder.HasOne(s => s.Plan)
            .WithMany()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Restrict); // Don't cascade-delete subscriptions when a plan is removed

        // RowVersion for optimistic concurrency.
        // When two processes try to update the same subscription simultaneously
        // (e.g., a billing event and a user cancellation arrive at the same time),
        // the second write will get a DbUpdateConcurrencyException instead of
        // silently overwriting the first. This is your defense against race conditions.
        builder.Property<uint>("RowVersion")
            .IsRowVersion();

        // Index: look up subscriptions by user — the most common query.
        // A user typically has 1-3 subscriptions (current + historical).
        builder.HasIndex(s => s.UserId);

        // Composite index: "find active subscriptions for a user"
        // Covers the query: WHERE UserId = @id AND Status = 'Active'
        builder.HasIndex(s => new { s.UserId, s.Status });
    }
}
