using FiscalDocs.Domain.Entities;
using FiscalDocs.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.Infrastructure.Persistence;

public sealed class FiscalDocsDbContext(DbContextOptions<FiscalDocsDbContext> options) : DbContext(options)
{
    public DbSet<FiscalDocument> Documents => Set<FiscalDocument>();
    public DbSet<DailyIssuerAggregate> DailyAggregates => Set<DailyIssuerAggregate>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FiscalDocsDbContext).Assembly);
}
