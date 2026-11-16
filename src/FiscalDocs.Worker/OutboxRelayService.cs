using FiscalDocs.Infrastructure.Messaging;

namespace FiscalDocs.Worker;

public sealed class OutboxRelayService(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILoggerFactory loggerFactory) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Outbox:PollIntervalSeconds", 1);
        var relay = new OutboxRelay(
            scopes, TimeSpan.FromSeconds(seconds), loggerFactory.CreateLogger<OutboxRelay>());
        return relay.RunAsync(stoppingToken);
    }
}
