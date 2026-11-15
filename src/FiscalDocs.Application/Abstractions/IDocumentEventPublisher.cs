using FiscalDocs.Application.Messaging;

namespace FiscalDocs.Application.Abstractions;

public interface IDocumentEventPublisher
{
    /// <summary>Completes only after the broker has confirmed the message; throws when it cannot.</summary>
    Task PublishAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken);
}
