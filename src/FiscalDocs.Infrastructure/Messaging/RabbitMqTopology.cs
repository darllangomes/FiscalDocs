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

    // Declares everything a publisher needs. The main queue dead-letters to the DLQ,
    // so a consumer only has to nack(requeue: false) to park a message there.
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

    // One queue per attempt. A message waits there until the TTL expires and is then
    // dead-lettered back to the main queue.
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

    // Attempt numbers start at 1.
    public static string RetryQueueName(int attempt) => RetryQueuePrefix + attempt;

    public static IEnumerable<string> RetryQueueNames(int count) =>
        Enumerable.Range(1, count).Select(RetryQueueName);
}
