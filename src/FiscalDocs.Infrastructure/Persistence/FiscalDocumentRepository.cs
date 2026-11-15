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

    private static bool IsAccessKeyViolation(DbUpdateException ex) =>
    ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: AccessKeyIndexName
    };
}
