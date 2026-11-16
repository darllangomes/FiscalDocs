using FiscalDocs.Application.Abstractions;

namespace FiscalDocs.Application.Documents;

public sealed class GetDocumentXmlHandler(IFiscalDocumentRepository repository)
{
    public Task<DocumentXml?> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindXmlByIdAsync(id, cancellationToken);
}
