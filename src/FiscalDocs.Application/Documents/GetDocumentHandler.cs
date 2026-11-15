using FiscalDocs.Application.Abstractions;

namespace FiscalDocs.Application.Documents;

public sealed class GetDocumentHandler(IFiscalDocumentRepository repository)
{
    public Task<DocumentDetails?> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindDetailsByIdAsync(id, cancellationToken);
}
