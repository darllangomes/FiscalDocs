using FiscalDocs.Application.Documents;

namespace FiscalDocs.Api.Contracts;

public sealed record DocumentSummaryResponse(
    Guid Id, string Type, string Status, string AccessKey,
    string Number, string Series, DateTimeOffset IssueDate,
    string IssuerCnpj, string IssuerName, string IssuerState,
    decimal TotalAmount, DateTimeOffset UpdatedAt)
{
    public static DocumentSummaryResponse From(DocumentSummary d) => new(
        d.Id, d.Type.ToString(), d.Status.ToString(), d.AccessKey, d.Number, d.Series, d.IssueDate,
        d.IssuerCnpj, d.IssuerName, d.IssuerState, d.TotalAmount, d.UpdatedAt);
}

public sealed record ListDocumentsResponse(
    IReadOnlyList<DocumentSummaryResponse> Items, int Page, int PageSize, int TotalCount)
{
    public static ListDocumentsResponse From(PagedResult<DocumentSummary> r) => new(
        r.Items.Select(DocumentSummaryResponse.From).ToList(), r.Page, r.PageSize, r.TotalCount);
}
