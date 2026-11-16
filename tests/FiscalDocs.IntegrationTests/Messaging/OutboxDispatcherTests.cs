using System.Collections.Concurrent;
using System.Text.Json;
using FiscalDocs.Application.Abstractions;
using FiscalDocs.Application.Messaging;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class OutboxDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public async Task SetUp()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
    }

    private static DocumentIngestedEvent NewEvent(int minuteOffset = 0, decimal total = 1500.00m) => new(
        MessageId: Guid.NewGuid(),
        DocumentId: Guid.NewGuid(),
        Type: DocumentType.NFe,
        IssuerDocument: "11222333000181",
        IssuerState: "PE",
        IssueDate: new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero),
        TotalAmount: total,
        OccurredAt: Now.AddMinutes(minuteOffset));

    private static async Task AddPendingAsync(params DocumentIngestedEvent[] events)
    {
        await using var context = InfrastructureFixture.CreateContext();
        context.OutboxMessages.AddRange(events.Select(OutboxMessage.From));
        await context.SaveChangesAsync();
    }

    private static async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
    }

    private static async Task<int> DispatchAsync(
        IDocumentEventPublisher publisher, int batchSize = 50, DateTimeOffset? now = null)
    {
        await using var context = InfrastructureFixture.CreateContext();
        var dispatcher = new OutboxDispatcher(
            context, publisher, new FixedClock(now ?? Now), NullLogger<OutboxDispatcher>.Instance);
        return await dispatcher.DispatchBatchAsync(batchSize, CancellationToken.None);
    }

    [Test]
    public async Task Dispatch_PendingMessages_PublishesThemOldestFirst()
    {
        // Arrange
        var newest = NewEvent(minuteOffset: 2);
        var oldest = NewEvent(minuteOffset: 0);
        var middle = NewEvent(minuteOffset: 1);
        await AddPendingAsync(newest, oldest, middle);
        var publisher = new RecordingPublisher();

        // Act
        var dispatched = await DispatchAsync(publisher);

        // Assert
        dispatched.Should().Be(3);
        publisher.Published.Select(e => e.MessageId).Should()
            .Equal(oldest.MessageId, middle.MessageId, newest.MessageId);
    }

    [Test]
    public async Task Dispatch_PublishedEvent_IsTheStoredOneWithTheSameMessageId()
    {
        // Arrange
        var stored = NewEvent(total: 321.45m);
        await AddPendingAsync(stored);
        var publisher = new RecordingPublisher();

        // Act
        await DispatchAsync(publisher);

        // Assert
        publisher.Published.Should().ContainSingle().Which.Should().BeEquivalentTo(stored);
    }

    [Test]
    public async Task Dispatch_PublishedMessages_AreMarkedAsProcessedWithTheCurrentTime()
    {
        // Arrange
        await AddPendingAsync(NewEvent());
        var processedAt = Now.AddMinutes(5);

        // Act
        await DispatchAsync(new RecordingPublisher(), now: processedAt);

        // Assert
        (await OutboxAsync()).Should().ContainSingle().Which.ProcessedAt.Should().Be(processedAt);
    }

    [Test]
    public async Task Dispatch_NothingPending_ReturnsZeroAndPublishesNothing()
    {
        // Arrange
        var publisher = new RecordingPublisher();

        // Act
        var dispatched = await DispatchAsync(publisher);

        // Assert
        dispatched.Should().Be(0);
        publisher.Published.Should().BeEmpty();
    }

    [Test]
    public async Task Dispatch_AlreadyProcessedMessages_AreNotPublishedAgain()
    {
        // Arrange
        await AddPendingAsync(NewEvent());
        await DispatchAsync(new RecordingPublisher());
        var publisher = new RecordingPublisher();

        // Act
        var dispatched = await DispatchAsync(publisher);

        // Assert
        dispatched.Should().Be(0);
        publisher.Published.Should().BeEmpty();
    }

    [Test]
    public async Task Dispatch_BatchSize_LimitsHowManyAreSentPerRun()
    {
        // Arrange
        await AddPendingAsync(Enumerable.Range(0, 5).Select(i => NewEvent(minuteOffset: i)).ToArray());
        var publisher = new RecordingPublisher();

        // Act
        var dispatched = await DispatchAsync(publisher, batchSize: 2);

        // Assert
        dispatched.Should().Be(2);
        (await OutboxAsync()).Count(m => m.ProcessedAt is null).Should().Be(3);
    }

    [Test]
    public async Task Dispatch_PublisherFails_KeepsTheFailedAndLaterMessagesPending()
    {
        // Arrange
        var first = NewEvent(minuteOffset: 0);
        var second = NewEvent(minuteOffset: 1);
        var third = NewEvent(minuteOffset: 2);
        await AddPendingAsync(first, second, third);
        var publisher = new RecordingPublisher { FailOn = second.MessageId };

        // Act
        var dispatched = await DispatchAsync(publisher);

        // Assert
        dispatched.Should().Be(1);
        var pending = (await OutboxAsync()).Where(m => m.ProcessedAt is null).Select(m => m.Id);
        pending.Should().BeEquivalentTo(new[] { second.MessageId, third.MessageId });
    }

    [Test]
    public async Task Dispatch_AfterAFailure_TheNextRunDeliversTheRest()
    {
        // Arrange
        var first = NewEvent(minuteOffset: 0);
        var second = NewEvent(minuteOffset: 1);
        await AddPendingAsync(first, second);
        var publisher = new RecordingPublisher { FailOn = second.MessageId };
        await DispatchAsync(publisher);
        publisher.FailOn = null;

        // Act
        var dispatched = await DispatchAsync(publisher);

        // Assert
        dispatched.Should().Be(1);
        publisher.Published.Select(e => e.MessageId).Should().Equal(first.MessageId, second.MessageId);
        (await OutboxAsync()).Should().OnlyContain(m => m.ProcessedAt != null);
    }

    [Test]
    public async Task Dispatch_ConcurrentDispatchers_PublishEachMessageOnce()
    {
        // Arrange
        await AddPendingAsync(Enumerable.Range(0, 30).Select(i => NewEvent(minuteOffset: i)).ToArray());
        var publisher = new RecordingPublisher(delay: TimeSpan.FromMilliseconds(5));

        // Act
        var runs = Enumerable.Range(0, 4).Select(async _ =>
        {
            while (await DispatchAsync(publisher, batchSize: 5) > 0)
            {
            }
        });
        await Task.WhenAll(runs);

        // Assert
        publisher.Published.Should().HaveCount(30);
        publisher.Published.Select(e => e.MessageId).Should().OnlyHaveUniqueItems();
        (await OutboxAsync()).Should().OnlyContain(m => m.ProcessedAt != null);
    }

    [Test]
    public async Task Dispatch_WithTheRealBroker_DeliversTheEventToTheQueue()
    {
        // Arrange
        await using var broker = await RabbitMqTestClient.ConnectAndPurgeAsync();
        await using var connections = new RabbitMqConnectionProvider(
            new RabbitMqOptions { Uri = InfrastructureFixture.RabbitMqUri });
        await using var publisher = new RabbitMqDocumentEventPublisher(connections);
        var stored = NewEvent();
        await AddPendingAsync(stored);

        // Act
        await DispatchAsync(publisher);

        // Assert
        var messages = await broker.ReceiveAsync(1, TimeSpan.FromSeconds(10));
        messages.Should().ContainSingle();
        messages[0].Properties.MessageId.Should().Be(stored.MessageId.ToString());
        JsonDocument.Parse(messages[0].Body).RootElement.GetProperty("documentId").GetGuid()
            .Should().Be(stored.DocumentId);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class RecordingPublisher(TimeSpan? delay = null) : IDocumentEventPublisher
    {
        private readonly ConcurrentQueue<DocumentIngestedEvent> _published = new();

        public IReadOnlyCollection<DocumentIngestedEvent> Published => _published.ToArray();

        public Guid? FailOn { get; set; }

        public async Task PublishAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken)
        {
            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            if (@event.MessageId == FailOn)
            {
                throw new InvalidOperationException("broker down");
            }

            _published.Enqueue(@event);
        }
    }
}
