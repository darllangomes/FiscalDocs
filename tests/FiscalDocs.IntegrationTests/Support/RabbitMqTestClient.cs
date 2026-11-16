using System.Text;
using FiscalDocs.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace FiscalDocs.IntegrationTests.Support;

public sealed record ReceivedMessage(string Body, IReadOnlyBasicProperties Properties);

public sealed class RabbitMqTestClient : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqTestClient(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqTestClient> ConnectAndPurgeAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(InfrastructureFixture.RabbitMqUri) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await RabbitMqTopology.DeclareAsync(channel, CancellationToken.None);
        await RabbitMqTopology.DeclareRetryAsync(channel, InfrastructureFixture.RetryDelays, CancellationToken.None);

        foreach (var queue in AllQueues)
        {
            await channel.QueuePurgeAsync(queue);
        }

        return new RabbitMqTestClient(connection, channel);
    }

    private static IEnumerable<string> AllQueues =>
        new[] { RabbitMqTopology.DocumentIngestedQueue, RabbitMqTopology.DeadLetterQueue }
            .Concat(RabbitMqTopology.RetryQueueNames(InfrastructureFixture.RetryDelays.Count));

    public Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(int expected, TimeSpan timeout) =>
        ReceiveAsync(RabbitMqTopology.DocumentIngestedQueue, expected, timeout);

    public Task<IReadOnlyList<ReceivedMessage>> ReceiveDeadLettersAsync(int expected, TimeSpan timeout) =>
        ReceiveAsync(RabbitMqTopology.DeadLetterQueue, expected, timeout);

    // Ready messages only: unacked ones are not counted.
    public async Task<int> CountRetryMessagesAsync()
    {
        var total = 0;
        foreach (var queue in RabbitMqTopology.RetryQueueNames(InfrastructureFixture.RetryDelays.Count))
        {
            total += (int)(await _channel.QueueDeclarePassiveAsync(queue)).MessageCount;
        }

        return total;
    }

    private async Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(string queue, int expected, TimeSpan timeout)
    {
        var messages = new List<ReceivedMessage>();
        var deadline = DateTime.UtcNow + timeout;

        while (messages.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = await _channel.BasicGetAsync(queue, autoAck: true);
            if (result is null)
            {
                await Task.Delay(50);
                continue;
            }

            messages.Add(new ReceivedMessage(Encoding.UTF8.GetString(result.Body.Span), result.BasicProperties));
        }

        return messages;
    }

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveAllAsync() =>
        await ReceiveAsync(int.MaxValue, TimeSpan.FromMilliseconds(300));

    public async Task PublishRawAsync(string body) =>
        await _channel.BasicPublishAsync(
            RabbitMqTopology.Exchange,
            RabbitMqTopology.DocumentIngestedRoutingKey,
            mandatory: true,
            basicProperties: new BasicProperties { DeliveryMode = DeliveryModes.Persistent },
            body: Encoding.UTF8.GetBytes(body));

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
