using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Application.Messaging;

public sealed record DocumentIngestedEvent(
    Guid MessageId,
    Guid DocumentId,
    DocumentType Type,
    string IssuerCnpj,
    string IssuerState,
    DateTimeOffset IssueDate,
    decimal TotalAmount,
    DateTimeOffset OccurredAt);
