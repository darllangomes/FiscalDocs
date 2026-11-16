using FiscalDocs.Application.Messaging;

namespace FiscalDocs.Application.Abstractions;

public interface IDocumentEventPublisher
{
    /// <summary>Só conclui depois que o broker confirmar a mensagem; lança exceção quando não consegue.</summary>
    Task PublishAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken);
}
