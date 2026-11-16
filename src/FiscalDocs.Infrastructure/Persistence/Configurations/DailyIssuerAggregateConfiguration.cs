using FiscalDocs.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FiscalDocs.Infrastructure.Persistence.Configurations;

public sealed class DailyIssuerAggregateConfiguration : IEntityTypeConfiguration<DailyIssuerAggregate>
{
    public void Configure(EntityTypeBuilder<DailyIssuerAggregate> builder)
    {
        builder.ToTable("daily_issuer_aggregates");

        builder.HasKey(a => new { a.Date, a.IssuerDocument, a.IssuerState });

        builder.Property(a => a.Date).HasColumnName("date");
        builder.Property(a => a.IssuerDocument).HasColumnName("issuer_document").HasMaxLength(14);
        builder.Property(a => a.IssuerState).HasColumnName("issuer_state").HasMaxLength(2);
        builder.Property(a => a.DocumentCount).HasColumnName("document_count").IsRequired();
        builder.Property(a => a.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 2).IsRequired();
    }
}
