using FiscalDocs.Infrastructure.Messaging;

namespace FiscalDocs.Worker;

public sealed class OutboxCleanupService(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILoggerFactory loggerFactory) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new OutboxCleanupOptions(
            TimeSpan.FromDays(configuration.GetValue("Outbox:RetentionDays", OutboxCleanupOptions.DefaultRetentionDays)),
            TimeSpan.FromMinutes(configuration.GetValue("Outbox:CleanupIntervalMinutes", OutboxCleanupOptions.DefaultIntervalMinutes)),
            configuration.GetValue("Outbox:CleanupBatchSize", OutboxCleanupOptions.DefaultBatchSize));

        return new OutboxCleanup(scopes, options, loggerFactory.CreateLogger<OutboxCleanup>()).RunAsync(stoppingToken);
    }
}
