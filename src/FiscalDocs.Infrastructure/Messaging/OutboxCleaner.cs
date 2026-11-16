using FiscalDocs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class OutboxCleaner(
    FiscalDocsDbContext context,
    TimeProvider clock,
    ILogger<OutboxCleaner> logger)
{
    /// <summary>
    /// Apaga as mensagens publicadas há mais que <paramref name="retention"/>, em lotes de <paramref name="batchSize"/>,
    /// e devolve quantas apagou. Nunca toca nas pendentes (<c>processed_at</c> nulo): apagá-las perderia o evento.
    /// </summary>
    public async Task<int> DeleteProcessedAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);

        var cutoff = (clock.GetUtcNow() - retention).UtcDateTime;
        var total = 0;
        int deleted;

        // Lotes curtos mantêm cada DELETE e seus locks pequenos, mesmo com milhões de linhas acumuladas.
        do
        {
            deleted = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM outbox_messages
                WHERE id IN (
                    SELECT id FROM outbox_messages
                    WHERE processed_at IS NOT NULL AND processed_at < {cutoff}
                    LIMIT {batchSize})
                """,
                cancellationToken);
            total += deleted;
        }
        while (deleted == batchSize);

        if (total > 0)
            logger.LogInformation("Deleted {Count} processed outbox message(s) older than {Retention}.", total, retention);

        return total;
    }
}
