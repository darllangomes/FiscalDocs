using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FiscalDocs.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FiscalDocsDbContext>
{
    public FiscalDocsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FISCALDOCS_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set the FISCALDOCS_CONNECTION environment variable before running dotnet ef.");

        var options = new DbContextOptionsBuilder<FiscalDocsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FiscalDocsDbContext(options);
    }
}
