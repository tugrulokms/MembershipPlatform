using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MembershipService.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.Payload)
            .IsRequired();

        // The background worker queries: "give me unprocessed messages, oldest first"
        // This index covers that exact query: WHERE ProcessedAt IS NULL ORDER BY CreatedAt
        builder.HasIndex(o => o.ProcessedAt)
            .HasFilter("\"ProcessedAt\" IS NULL");
    }
}
