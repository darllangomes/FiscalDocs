namespace FiscalDocs.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Uri { get; init; } = string.Empty;

    public int ConnectionTimeoutSeconds { get; init; } = 5;
}
