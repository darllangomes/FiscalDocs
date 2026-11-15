using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FiscalDocs.Infrastructure.Persistence.Configurations;

public sealed class FiscalDocumentConfiguration : IEntityTypeConfiguration<FiscalDocument>
{
    public void Configure(EntityTypeBuilder<FiscalDocument> builder)
    {
        builder.ToTable("fiscal_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Type)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(d => d.AccessKey)
            .HasConversion(v => v.Value, s => AccessKey.Parse(s))
            .HasMaxLength(44)
            .IsRequired();

        builder.Property(d => d.Number).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Series).HasMaxLength(10).IsRequired();

        // Npgsql only accepts offset 0 for timestamptz; the original offset stays in RawXml.
        builder.Property(d => d.IssueDate)
            .HasConversion(v => v.ToUniversalTime(), v => v)
            .IsRequired();

        builder.Property(d => d.IssuerCnpj)
            .HasConversion(v => v.Value, s => Cnpj.Parse(s))
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(d => d.IssuerName).HasMaxLength(200).IsRequired();
        builder.Property(d => d.IssuerState).HasMaxLength(2).IsRequired();

        builder.Property(d => d.RecipientDocument).HasMaxLength(20);
        builder.Property(d => d.RecipientName).HasMaxLength(200);
        builder.Property(d => d.RecipientState).HasMaxLength(2);

        builder.Property(d => d.TotalAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(d => d.XmlHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.RawXml).HasColumnType("text").IsRequired();

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(d => d.Notes).HasMaxLength(500);

        builder.Property(d => d.IsDeleted).IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasConversion(v => v.ToUniversalTime(), v => v)
            .IsRequired();
        builder.Property(d => d.UpdatedAt)
            .HasConversion(v => v.ToUniversalTime(), v => v)
            .IsRequired();

        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasQueryFilter(d => !d.IsDeleted);

        builder.HasIndex(d => d.AccessKey).IsUnique();
        builder.HasIndex(d => new { d.IssuerCnpj, d.IssueDate });
        builder.HasIndex(d => new { d.IssuerState, d.IssueDate });
        builder.HasIndex(d => d.IssueDate);
    }
}
