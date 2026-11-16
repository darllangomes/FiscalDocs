using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class OutboxRelay(
    IServiceScopeFactory scopes,
    TimeSpan pollInterval,
    ILogger<OutboxRelay> logger)
{
    private const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(cancellationToken);
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox relay iteration failed; retrying in {Interval}.", pollInterval);
                await WaitAsync(cancellationToken);
            }
        }
    }

    // Lote cheio indica que pode haver mais; lote curto indica tabela esgotada ou falha na publicação.
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        int sent;
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
            sent = await dispatcher.DispatchBatchAsync(BatchSize, cancellationToken);
        }
        while (sent == BatchSize);
    }

    private async Task WaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(pollInterval, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
