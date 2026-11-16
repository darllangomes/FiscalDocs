using System.Text.Json;
using FiscalDocs.Application.Messaging;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using RabbitMQ.Client;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class RabbitMqEventPublisherTests
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    private RabbitMqTestClient _broker = null!;
    private RabbitMqConnectionProvider _connections = null!;
    private RabbitMqDocumentEventPublisher _publisher = null!;

    [SetUp]
    public async Task SetUp()
    {
        _broker = await RabbitMqTestClient.ConnectAndPurgeAsync();
        _connections = new RabbitMqConnectionProvider(new RabbitMqOptions { Uri = InfrastructureFixture.RabbitMqUri });
        _publisher = new RabbitMqDocumentEventPublisher(_connections);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _publisher.DisposeAsync();
        await _connections.DisposeAsync();
        await _broker.DisposeAsync();
    }

    private static DocumentIngestedEvent NewEvent() => new(
        MessageId: Guid.NewGuid(),
        DocumentId: Guid.NewGuid(),
        Type: DocumentType.NFe,
        IssuerCnpj: "11222333000181",
        IssuerState: "PE",
        IssueDate: new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
        TotalAmount: 1500.00m,
        OccurredAt: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task Publish_DeliversTheEventAsJsonToTheQueue()
    {
        // Arrange
        var @event = NewEvent();

        // Act
        await _publisher.PublishAsync(@event, CancellationToken.None);

        // Assert
        var messages = await _broker.ReceiveAsync(1, ReceiveTimeout);
        messages.Should().ContainSingle();
        var json = JsonDocument.Parse(messages[0].Body).RootElement;
        json.GetProperty("messageId").GetGuid().Should().Be(@event.MessageId);
        json.GetProperty("documentId").GetGuid().Should().Be(@event.DocumentId);
        json.GetProperty("type").GetString().Should().Be("NFe");
        json.GetProperty("issuerCnpj").GetString().Should().Be("11222333000181");
        json.GetProperty("issuerState").GetString().Should().Be("PE");
        json.GetProperty("issueDate").GetDateTimeOffset().Should().Be(@event.IssueDate);
        json.GetProperty("totalAmount").GetDecimal().Should().Be(1500.00m);
        json.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(@event.OccurredAt);
    }

    [Test]
    public async Task Publish_SendsPersistentMessageWithIdentifyingProperties()
    {
        // Arrange
        var @event = NewEvent();

        // Act
        await _publisher.PublishAsync(@event, CancellationToken.None);

        // Assert
        var message = (await _broker.ReceiveAsync(1, ReceiveTimeout)).Single();
        message.Properties.DeliveryMode.Should().Be(DeliveryModes.Persistent);
        message.Properties.ContentType.Should().Be("application/json");
        message.Properties.MessageId.Should().Be(@event.MessageId.ToString());
        message.Properties.Type.Should().Be("DocumentIngested");
    }

    [Test]
    public async Task Publish_Body_CarriesOnlyTheContractFields()
    {
        // Act
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);

        // Assert
        var message = (await _broker.ReceiveAsync(1, ReceiveTimeout)).Single();
        var names = JsonDocument.Parse(message.Body).RootElement.EnumerateObject().Select(p => p.Name);
        names.Should().BeEquivalentTo(
            "messageId", "documentId", "type", "issuerCnpj", "issuerState", "issueDate", "totalAmount", "occurredAt");
    }

    [Test]
    public async Task Publish_ManyConcurrently_DeliversEveryEventOnce()
    {
        // Arrange
        var events = Enumerable.Range(0, 20).Select(_ => NewEvent()).ToList();

        // Act
        await Task.WhenAll(events.Select(e => _publisher.PublishAsync(e, CancellationToken.None)));

        // Assert
        var messages = await _broker.ReceiveAsync(20, ReceiveTimeout);
        messages.Select(m => m.Properties.MessageId)
            .Should().BeEquivalentTo(events.Select(e => e.MessageId.ToString()));
    }

    [Test]
    public async Task Publish_FromTwoPublishers_DeclaringTheTopologyTwiceIsHarmless()
    {
        // Arrange
        await using var otherConnections = new RabbitMqConnectionProvider(
            new RabbitMqOptions { Uri = InfrastructureFixture.RabbitMqUri });
        await using var other = new RabbitMqDocumentEventPublisher(otherConnections);

        // Act
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await other.PublishAsync(NewEvent(), CancellationToken.None);

        // Assert
        (await _broker.ReceiveAsync(2, ReceiveTimeout)).Should().HaveCount(2);
    }

    [Test]
    public async Task Publish_AfterTheBrokerDropsEveryConnection_DeliversOnceItIsBack()
    {
        // Arrange
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await _broker.ReceiveAsync(1, ReceiveTimeout);
        await InfrastructureFixture.CloseRabbitMqConnectionsAsync();
        await using var checker = await RabbitMqTestClient.ConnectAndPurgeAsync();
        var @event = NewEvent();

        // Act
        var deadline = DateTime.UtcNow + ReceiveTimeout;
        while (true)
        {
            try
            {
                await _publisher.PublishAsync(@event, CancellationToken.None);
                break;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }

        // Assert
        var messages = await checker.ReceiveAsync(1, ReceiveTimeout);
        messages.Should().ContainSingle().Which.Properties.MessageId.Should().Be(@event.MessageId.ToString());
    }

    [Test]
    public async Task Publish_WhenTheBrokerIsUnreachable_Throws()
    {
        // Arrange
        await using var connections = new RabbitMqConnectionProvider(new RabbitMqOptions
        {
            Uri = "amqp://guest:guest@127.0.0.1:1/",
            ConnectionTimeoutSeconds = 2
        });
        await using var publisher = new RabbitMqDocumentEventPublisher(connections);

        // Act
        var act = () => publisher.PublishAsync(NewEvent(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }
}
