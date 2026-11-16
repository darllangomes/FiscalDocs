using FiscalDocs.Infrastructure.Messaging;

namespace FiscalDocs.Worker;

public sealed class Worker(DocumentIngestedConsumer consumer, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await consumer.StartAsync(stoppingToken);
        logger.LogInformation("Consuming from {Queue}.", RabbitMqTopology.DocumentIngestedQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await consumer.DisposeAsync();
    }
}
