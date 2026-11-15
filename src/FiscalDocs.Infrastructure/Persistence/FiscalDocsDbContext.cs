using FiscalDocs.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.Infrastructure.Persistence;

public sealed class FiscalDocsDbContext(DbContextOptions<FiscalDocsDbContext> options) : DbContext(options)
{
    public DbSet<FiscalDocument> Documents => Set<FiscalDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FiscalDocsDbContext).Assembly);
}
