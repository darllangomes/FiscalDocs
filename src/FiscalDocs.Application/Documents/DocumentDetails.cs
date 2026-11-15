using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Application.Documents;

public sealed record DocumentDetails(
    Guid Id, DocumentType Type, DocumentStatus Status, string AccessKey,
    string Number, string Series, DateTimeOffset IssueDate,
    string IssuerCnpj, string IssuerName, string IssuerState,
    string? RecipientName, string? RecipientState, decimal TotalAmount,
    string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    uint Version);
