using MembershipService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MembershipService.Infrastructure.Persistence.Configurations;

public class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FeatureKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Unique constraint: a user can only have one entitlement per feature key.
        // Without this, a bug could grant "advanced_analytics" twice to the same user.
        // The database enforces what your code should guarantee — defense in depth.
        builder.HasIndex(e => new { e.UserId, e.FeatureKey })
            .IsUnique();

        // Index: "what entitlements does this user have?" — the authorization hot path.
        // This query runs on every API request that checks feature access.
        builder.HasIndex(e => e.UserId);

        // Index: "what entitlements were granted by this subscription?"
        // Needed for bulk revocation when a subscription expires.
        builder.HasIndex(e => e.SubscriptionId);
    }
}
