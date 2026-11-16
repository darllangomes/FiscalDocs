namespace FiscalDocs.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Uri { get; init; } = string.Empty;

    public int ConnectionTimeoutSeconds { get; init; } = 5;

    // One entry per retry; a message is delivered at most RetryDelays.Count + 1 times.
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2) };
}
