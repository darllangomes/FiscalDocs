using FiscalDocs.Application.Abstractions;
using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FiscalDocs.Infrastructure.Persistence;

public sealed class FiscalDocumentRepository : IFiscalDocumentRepository
{

    private const string AccessKeyIndexName = "IX_fiscal_documents_AccessKey";
    private readonly FiscalDocsDbContext _context;



    public FiscalDocumentRepository(FiscalDocsDbContext context) => _context = context;

    public async Task<bool> TryAddAsync(FiscalDocument document, CancellationToken cancellationToken)
    {
        _context.Documents.Add(document);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsAccessKeyViolation(ex))
        {
            _context.Entry(document).State = EntityState.Detached;
            return false;
        }
    }

    public Task<FiscalDocument?> FindByAccessKeyAsync(AccessKey accessKey, CancellationToken cancellationToken)
    {
        return _context.Documents
         .IgnoreQueryFilters()
         .AsNoTracking()
         .FirstOrDefaultAsync(d => d.AccessKey == accessKey, cancellationToken);
    }

    public Task<DocumentDetails?> FindDetailsByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentDetails(
                d.Id, d.Type, d.Status, d.AccessKey.Value, d.Number, d.Series, d.IssueDate,
                d.IssuerCnpj.Value, d.IssuerName, d.IssuerState, d.RecipientName, d.RecipientState,
                d.TotalAmount, d.Notes, d.CreatedAt, d.UpdatedAt,
                EF.Property<uint>(d, "xmin")))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<DocumentSummary>> ListAsync(ListDocumentsQuery query, CancellationToken cancellationToken)
    {
        var documents = _context.Documents.AsNoTracking();

        if (query.Type is { } type) documents = documents.Where(d => d.Type == type);
        if (query.Status is { } status) documents = documents.Where(d => d.Status == status);
        if (query.IssuerCnpj is { } cnpj) documents = documents.Where(d => d.IssuerCnpj == cnpj);
        if (query.IssuerState is { } state) documents = documents.Where(d => d.IssuerState == state);
        if (query.IssuedFrom is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            documents = documents.Where(d => d.IssueDate >= start);
        }
        if (query.IssuedTo is { } to && to < DateOnly.MaxValue)
        {
            var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            documents = documents.Where(d => d.IssueDate < endExclusive);
        }

        var total = await documents.CountAsync(cancellationToken);

        var items = await documents
            .OrderByDescending(d => d.IssueDate)
            .ThenBy(d => d.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(d => new DocumentSummary(
                d.Id, d.Type, d.Status, d.AccessKey.Value, d.Number, d.Series, d.IssueDate,
                d.IssuerCnpj.Value, d.IssuerName, d.IssuerState, d.TotalAmount, d.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentSummary>(items, query.Page, query.PageSize, total);
    }

    public async Task<UpdateDocumentResult> UpdateAsync(
        Guid id, uint expectedVersion, Action<FiscalDocument> apply, CancellationToken cancellationToken)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return new UpdateDocumentResult(UpdateDocumentOutcome.NotFound);
        }

        apply(document);

        _context.Entry(document).Property<uint>("xmin").OriginalValue = expectedVersion;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.Entry(document).State = EntityState.Detached;
            return new UpdateDocumentResult(UpdateDocumentOutcome.PreconditionFailed);
        }

        var details = await FindDetailsByIdAsync(id, cancellationToken);
        return new UpdateDocumentResult(UpdateDocumentOutcome.Updated, details);
    }

    public async Task<DeleteDocumentOutcome> DeleteAsync(
        Guid id, uint? expectedVersion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return DeleteDocumentOutcome.NotFound;
        }

        document.MarkAsDeleted(now);
        if (expectedVersion is { } version)
        {
            _context.Entry(document).Property<uint>("xmin").OriginalValue = version;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.Entry(document).State = EntityState.Detached;
            return DeleteDocumentOutcome.PreconditionFailed;
        }

        return DeleteDocumentOutcome.Deleted;
    }

    private static bool IsAccessKeyViolation(DbUpdateException ex) =>
    ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: AccessKeyIndexName
    };
}
