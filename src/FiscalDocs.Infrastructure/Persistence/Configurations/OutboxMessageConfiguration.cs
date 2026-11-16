using FiscalDocs.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FiscalDocs.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(m => m.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(v => v.ToUniversalTime(), v => v)
            .IsRequired();
        builder.Property(m => m.ProcessedAt)
            .HasColumnName("processed_at")
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime() : (DateTimeOffset?)null,
                v => v);

        builder.HasIndex(m => new { m.OccurredAt, m.Id })
            .HasDatabaseName("IX_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}
