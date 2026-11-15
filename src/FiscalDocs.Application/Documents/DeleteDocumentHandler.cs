using FiscalDocs.Application.Abstractions;

namespace FiscalDocs.Application.Documents;

public sealed class DeleteDocumentHandler(IFiscalDocumentRepository repository, TimeProvider clock)
{
    public Task<DeleteDocumentOutcome> HandleAsync(
        Guid id, uint? expectedVersion, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, expectedVersion, clock.GetUtcNow(), cancellationToken);
}
