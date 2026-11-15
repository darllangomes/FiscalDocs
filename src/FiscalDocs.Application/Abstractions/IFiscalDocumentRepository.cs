using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Application.Abstractions;

public interface IFiscalDocumentRepository
{
    /// <summary>Returns false when a document with the same access key already exists.</summary>
    Task<bool> TryAddAsync(FiscalDocument document, CancellationToken cancellationToken);

    /// <summary>Looks up by access key, including soft-deleted documents.</summary>
    Task<FiscalDocument?> FindByAccessKeyAsync(AccessKey accessKey, CancellationToken cancellationToken);

    /// <summary>Read model for the API; excludes soft-deleted documents and never loads the raw XML.</summary>
    Task<DocumentDetails?> FindDetailsByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Filtered, paged summaries, newest issue date first; excludes soft-deleted documents.</summary>
    Task<PagedResult<DocumentSummary>> ListAsync(ListDocumentsQuery query, CancellationToken cancellationToken);
}
