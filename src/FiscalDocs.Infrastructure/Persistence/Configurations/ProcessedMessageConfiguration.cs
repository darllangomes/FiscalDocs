using FiscalDocs.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FiscalDocs.Infrastructure.Persistence.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");

        builder.HasKey(m => m.MessageId);
        builder.Property(m => m.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        builder.Property(m => m.ProcessedAt)
            .HasColumnName("processed_at")
            .HasConversion(v => v.ToUniversalTime(), v => v)
            .IsRequired();
    }
}
