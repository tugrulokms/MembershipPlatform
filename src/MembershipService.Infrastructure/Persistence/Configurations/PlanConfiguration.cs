using MembershipService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MembershipService.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        // Store price as decimal(18,2) — sufficient precision for currency
        builder.Property(p => p.Price)
            .HasPrecision(18, 2);

        // Store enums as strings in the database.
        // Why? If you store as int, "Monthly" = 0. If someone reorders the enum
        // or inserts a value, every row's meaning silently changes.
        // String storage is self-describing and safe against enum reordering.
        builder.Property(p => p.Currency)
            .HasConversion<string>()
            .HasMaxLength(3);

        builder.Property(p => p.BillingPeriod)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.CreatedAt)
            .HasDefaultValueSql("NOW()");

        // Index: look up active plans for display to customers
        builder.HasIndex(p => p.IsActive);
    }
}
