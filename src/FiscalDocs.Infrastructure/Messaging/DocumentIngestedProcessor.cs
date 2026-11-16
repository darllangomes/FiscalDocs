using FiscalDocs.Application.Messaging;
using FiscalDocs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class DocumentIngestedProcessor(FiscalDocsDbContext context, TimeProvider clock)
    : IDocumentIngestedProcessor
{
    public async Task ProcessAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Claiming the MessageId first makes duplicates (even concurrent ones) a no-op:
        // a second insert waits for the first transaction and then conflicts.
        var processedAt = clock.GetUtcNow();
        var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO processed_messages (message_id, processed_at)
            VALUES ({@event.MessageId}, {processedAt})
            ON CONFLICT (message_id) DO NOTHING
            """,
            cancellationToken);

        if (claimed == 0)
        {
            return;
        }

        var date = DateOnly.FromDateTime(@event.IssueDate.UtcDateTime);

        // Atomic upsert in SQL: no read-modify-write, so concurrent events cannot lose updates.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO daily_issuer_aggregates (date, issuer_cnpj, issuer_state, document_count, total_amount)
            VALUES ({date}, {@event.IssuerCnpj}, {@event.IssuerState}, 1, {@event.TotalAmount})
            ON CONFLICT (date, issuer_cnpj, issuer_state) DO UPDATE
            SET document_count = daily_issuer_aggregates.document_count + 1,
                total_amount = daily_issuer_aggregates.total_amount + EXCLUDED.total_amount
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
