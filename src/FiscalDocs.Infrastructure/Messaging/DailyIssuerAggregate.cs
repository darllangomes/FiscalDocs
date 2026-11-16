namespace FiscalDocs.Infrastructure.Messaging;

public sealed class DailyIssuerAggregate
{
    public DateOnly Date { get; set; }
    public string IssuerDocument { get; set; } = string.Empty;
    public string IssuerState { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public decimal TotalAmount { get; set; }
}
