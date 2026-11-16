using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiscalDocs.Infrastructure.Messaging;

/// <summary>Repete a limpeza da outbox no intervalo configurado e sobrevive a falhas passageiras do banco.</summary>
public sealed class OutboxCleanup(
    IServiceScopeFactory scopes,
    OutboxCleanupOptions options,
    ILogger<OutboxCleanup> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<OutboxCleaner>();
                await cleaner.DeleteProcessedAsync(options.Retention, options.BatchSize, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox cleanup failed; retrying in {Interval}.", options.Interval);
            }

            try
            {
                await Task.Delay(options.Interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
