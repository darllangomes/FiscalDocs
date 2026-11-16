using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Api.Contracts;

public sealed record DocumentResponse(
    Guid Id, DocumentType Type, DocumentStatus Status, string AccessKey,
    string Number, string Series, DateTimeOffset IssueDate,
    string IssuerDocument, string IssuerName, string IssuerState,
    string? RecipientName, string? RecipientState, decimal TotalAmount,
    string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static DocumentResponse From(DocumentDetails d) => new(
        d.Id, d.Type, d.Status, d.AccessKey, d.Number, d.Series, d.IssueDate,
        d.IssuerDocument.Masked, d.IssuerName, d.IssuerState, d.RecipientName, d.RecipientState,
        d.TotalAmount, d.Notes, d.CreatedAt, d.UpdatedAt);
}
