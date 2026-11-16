using FiscalDocs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace FiscalDocs.IntegrationTests;

[SetUpFixture]
public class InfrastructureFixture
{
    private static PostgreSqlContainer? _postgres;
    private static RabbitMqContainer? _rabbitMq;

    public static string ConnectionString =>
        _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Postgres container is not started.");

    public static string RabbitMqUri =>
        _rabbitMq?.GetConnectionString()
        ?? throw new InvalidOperationException("RabbitMQ container is not started.");

    // Short delays so retry tests finish in seconds. Every consumer in the tests must use these:
    // redeclaring a queue with a different x-message-ttl fails with PRECONDITION_FAILED.
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } =
        new[] { TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(900) };

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16").Build();
        _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management").Build();
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_rabbitMq is not null)
        {
            await _rabbitMq.DisposeAsync();
        }
    }

    public static async Task CloseRabbitMqConnectionsAsync()
    {
        var result = await (_rabbitMq ?? throw new InvalidOperationException("RabbitMQ container is not started."))
            .ExecAsync(new[] { "rabbitmqctl", "close_all_connections", "test" });
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rabbitmqctl failed: {result.Stderr}");
        }
    }

    public static FiscalDocsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FiscalDocsDbContext>().UseNpgsql(ConnectionString).Options);
}
