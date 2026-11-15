using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Application.Documents;

/// <summary>Listing row: no raw XML and no notes.</summary>
public sealed record DocumentSummary(
    Guid Id, DocumentType Type, DocumentStatus Status, string AccessKey,
    string Number, string Series, DateTimeOffset IssueDate,
    string IssuerCnpj, string IssuerName, string IssuerState,
    decimal TotalAmount, DateTimeOffset UpdatedAt);
