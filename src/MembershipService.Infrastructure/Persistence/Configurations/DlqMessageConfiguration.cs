using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MembershipService.Infrastructure.Persistence.Configurations;

public class DlqMessageConfiguration : IEntityTypeConfiguration<DlqMessage>
{
    public void Configure(EntityTypeBuilder<DlqMessage> builder)
    {
        builder.HasIndex(m => m.EventId).IsUnique();
    }
}