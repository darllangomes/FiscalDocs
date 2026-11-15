using FiscalDocs.Application.Abstractions;

namespace FiscalDocs.Application.Documents;

public sealed class ListDocumentsHandler(IFiscalDocumentRepository repository)
{
    public Task<PagedResult<DocumentSummary>> HandleAsync(ListDocumentsQuery query, CancellationToken cancellationToken) =>
        repository.ListAsync(query, cancellationToken);
}
