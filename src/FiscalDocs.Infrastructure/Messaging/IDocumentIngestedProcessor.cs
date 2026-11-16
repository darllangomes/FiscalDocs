using FiscalDocs.Application.Messaging;

namespace FiscalDocs.Infrastructure.Messaging;

public interface IDocumentIngestedProcessor
{
    Task ProcessAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken);
}
