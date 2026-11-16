using FiscalDocs.Application.Abstractions;
using FiscalDocs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class OutboxDispatcher(
    FiscalDocsDbContext context,
    IDocumentEventPublisher publisher,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>
    /// Publishes up to <paramref name="batchSize"/> pending messages, oldest first, and returns how many
    /// were sent. Stops at the first failure so later messages stay pending and order is kept.
    /// </summary>
    public async Task<int> DispatchBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED: concurrent dispatchers take different rows instead of waiting or double-sending.
        var pending = await context.OutboxMessages
            .FromSqlInterpolated(
                $"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL
                ORDER BY occurred_at, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message.ToEvent(), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to publish outbox message {MessageId}; it stays pending.", message.Id);
                break;
            }

            message.ProcessedAt = clock.GetUtcNow();
            sent++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sent;
    }
}
