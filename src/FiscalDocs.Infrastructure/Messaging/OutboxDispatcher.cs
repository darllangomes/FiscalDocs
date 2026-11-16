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
    /// Publica até <paramref name="batchSize"/> mensagens pendentes, da mais antiga para a mais nova, e devolve quantas
    /// foram enviadas. Para na primeira falha: as seguintes continuam pendentes e a ordem é mantida.
    /// </summary>
    public async Task<int> DispatchBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED: dispatchers concorrentes pegam linhas diferentes, sem esperar nem enviar em duplicidade.
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
