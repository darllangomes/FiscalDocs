using FiscalDocs.Application.Messaging;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.Infrastructure.Persistence;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class DocumentIngestedConsumerTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private RabbitMqTestClient _broker = null!;
    private RabbitMqConnectionProvider _connections = null!;
    private RabbitMqDocumentEventPublisher _publisher = null!;
    private ServiceProvider _services = null!;
    private DocumentIngestedConsumer? _consumer;

    [SetUp]
    public async Task SetUp()
    {
        await using (var context = InfrastructureFixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE daily_issuer_aggregates, processed_messages");
        }

        _broker = await RabbitMqTestClient.ConnectAndPurgeAsync();
        _connections = new RabbitMqConnectionProvider(new RabbitMqOptions { Uri = InfrastructureFixture.RabbitMqUri });
        _publisher = new RabbitMqDocumentEventPublisher(_connections);

        _services = new ServiceCollection()
            .AddDbContext<FiscalDocsDbContext>(o => o.UseNpgsql(InfrastructureFixture.ConnectionString))
            .AddSingleton(TimeProvider.System)
            .AddScoped<DocumentIngestedProcessor>()
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_consumer is not null)
        {
            await _consumer.DisposeAsync();
        }

        await _publisher.DisposeAsync();
        await _connections.DisposeAsync();
        await _broker.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static DocumentIngestedEvent NewEvent(decimal total = 1500.00m) => new(
        MessageId: Guid.NewGuid(),
        DocumentId: Guid.NewGuid(),
        Type: DocumentType.NFe,
        IssuerCnpj: "11222333000181",
        IssuerState: "PE",
        IssueDate: new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero),
        TotalAmount: total,
        OccurredAt: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    private async Task StartConsumerAsync()
    {
        _consumer = new DocumentIngestedConsumer(
            _connections,
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentIngestedConsumer>.Instance);
        await _consumer.StartAsync(CancellationToken.None);
    }

    private async Task StopConsumerAsync()
    {
        await _consumer!.DisposeAsync();
        _consumer = null;
    }

    private static async Task<int> ProcessedCountAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.ProcessedMessages.CountAsync();
    }

    private static async Task<DailyIssuerAggregate?> AggregateAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.DailyAggregates.AsNoTracking().SingleOrDefaultAsync();
    }

    private static async Task WaitForProcessedAsync(int expected)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (DateTime.UtcNow < deadline && await ProcessedCountAsync() < expected)
        {
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task Consumer_PublishedEvent_CreatesTheAggregate()
    {
        // Arrange
        await StartConsumerAsync();

        // Act
        await _publisher.PublishAsync(NewEvent(total: 1500.00m), CancellationToken.None);
        await WaitForProcessedAsync(1);

        // Assert
        var aggregate = await AggregateAsync();
        aggregate.Should().NotBeNull();
        aggregate!.DocumentCount.Should().Be(1);
        aggregate.TotalAmount.Should().Be(1500.00m);
    }

    [Test]
    public async Task Consumer_EventPublishedBeforeItStarts_IsProcessedOnStart()
    {
        // Arrange
        await _publisher.PublishAsync(NewEvent(total: 300.00m), CancellationToken.None);

        // Act
        await StartConsumerAsync();
        await WaitForProcessedAsync(1);

        // Assert
        (await AggregateAsync())!.TotalAmount.Should().Be(300.00m);
    }

    [Test]
    public async Task Consumer_RedeliveredEvent_CountsItOnce()
    {
        // Arrange
        var duplicated = NewEvent(total: 1500.00m);
        await StartConsumerAsync();

        // Act
        await _publisher.PublishAsync(duplicated, CancellationToken.None);
        await _publisher.PublishAsync(duplicated, CancellationToken.None);
        await _publisher.PublishAsync(NewEvent(total: 10.00m), CancellationToken.None);
        await WaitForProcessedAsync(2);

        // Assert
        var aggregate = await AggregateAsync();
        aggregate!.DocumentCount.Should().Be(2);
        aggregate.TotalAmount.Should().Be(1510.00m);
    }

    [Test]
    public async Task Consumer_ProcessedEvent_IsAcknowledged()
    {
        // Arrange
        await StartConsumerAsync();
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await WaitForProcessedAsync(1);

        // Act
        // Unacked messages return to the queue when the consumer's channel closes.
        await StopConsumerAsync();

        // Assert
        (await _broker.ReceiveAllAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Consumer_MalformedMessage_DoesNotStopProcessingLaterMessages()
    {
        // Arrange
        await StartConsumerAsync();

        // Act
        await _broker.PublishRawAsync("this is not json");
        await _broker.PublishRawAsync("{\"messageId\":\"not-a-guid\"}");
        await _publisher.PublishAsync(NewEvent(total: 42.00m), CancellationToken.None);
        await WaitForProcessedAsync(1);

        // Assert
        (await AggregateAsync())!.TotalAmount.Should().Be(42.00m);
    }

    [Test]
    public async Task Consumer_MalformedMessage_IsNotRedeliveredForever()
    {
        // Arrange
        await StartConsumerAsync();
        await _broker.PublishRawAsync("this is not json");
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await WaitForProcessedAsync(1);

        // Act
        await StopConsumerAsync();

        // Assert
        (await _broker.ReceiveAllAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Consumer_StoppedWhileIdle_CompletesPromptly()
    {
        // Arrange
        await StartConsumerAsync();

        // Act
        var stop = _consumer!.DisposeAsync().AsTask();
        _consumer = null;

        // Assert
        (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(stop);
    }
}
