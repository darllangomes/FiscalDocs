using FiscalDocs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace FiscalDocs.IntegrationTests;

[SetUpFixture]
public class PostgresFixture
{
    private static PostgreSqlContainer? _container;

    public static string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Postgres container is not started.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16").Build();
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public static FiscalDocsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FiscalDocsDbContext>().UseNpgsql(ConnectionString).Options);
}
