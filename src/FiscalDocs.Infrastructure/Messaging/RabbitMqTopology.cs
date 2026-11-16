using RabbitMQ.Client;

namespace FiscalDocs.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public const string Exchange = "fiscaldocs.events";
    public const string DocumentIngestedQueue = "fiscaldocs.document-ingested";
    public const string DocumentIngestedRoutingKey = "document.ingested";
    public const string DocumentIngestedType = "DocumentIngested";

    public const string DeadLetterExchange = "fiscaldocs.dlx";
    public const string DeadLetterQueue = "fiscaldocs.document-ingested.dlq";
    private const string DeadLetterRoutingKey = "document.ingested.dead";

    private const string RetryQueuePrefix = "fiscaldocs.document-ingested.retry.";

    // Declara tudo o que o publicador precisa. A fila principal encaminha para a DLQ,
    // então basta o consumidor fazer nack(requeue: false) para estacionar a mensagem lá.
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            DeadLetterQueue, DeadLetterExchange, DeadLetterRoutingKey, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            DocumentIngestedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            DocumentIngestedQueue, Exchange, DocumentIngestedRoutingKey, cancellationToken: cancellationToken);
    }

    // Uma fila por tentativa. A mensagem espera ali até o TTL expirar e então volta
    // para a fila principal via dead-letter.
    public static async Task DeclareRetryAsync(
        IChannel channel, IReadOnlyList<TimeSpan> delays, CancellationToken cancellationToken)
    {
        for (var i = 0; i < delays.Count; i++)
        {
            await channel.QueueDeclareAsync(
                RetryQueueName(i + 1),
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (int)delays[i].TotalMilliseconds,
                    ["x-dead-letter-exchange"] = Exchange,
                    ["x-dead-letter-routing-key"] = DocumentIngestedRoutingKey
                },
                cancellationToken: cancellationToken);
        }
    }

    public static string RetryQueueName(int attempt) => RetryQueuePrefix + attempt;

    public static IEnumerable<string> RetryQueueNames(int count) =>
        Enumerable.Range(1, count).Select(RetryQueueName);
}
