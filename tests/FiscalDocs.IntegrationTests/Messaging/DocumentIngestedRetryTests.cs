using FiscalDocs.Application.Messaging;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class DocumentIngestedRetryTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);

    private static int MaxDeliveries => InfrastructureFixture.RetryDelays.Count + 1;

    private RabbitMqTestClient _broker = null!;
    private RabbitMqConnectionProvider _connections = null!;
    private RabbitMqDocumentEventPublisher _publisher = null!;
    private ServiceProvider _services = null!;
    private ScriptedProcessor _processor = null!;
    private DocumentIngestedConsumer? _consumer;

    [SetUp]
    public async Task SetUp()
    {
        _broker = await RabbitMqTestClient.ConnectAndPurgeAsync();
        _connections = new RabbitMqConnectionProvider(new RabbitMqOptions
        {
            Uri = InfrastructureFixture.RabbitMqUri,
            RetryDelays = InfrastructureFixture.RetryDelays
        });
        _publisher = new RabbitMqDocumentEventPublisher(_connections);
        _processor = new ScriptedProcessor();
        _services = new ServiceCollection()
            .AddSingleton<IDocumentIngestedProcessor>(_processor)
            .BuildServiceProvider();

        _consumer = new DocumentIngestedConsumer(
            _connections,
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentIngestedConsumer>.Instance);
        await _consumer.StartAsync(CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _consumer!.DisposeAsync();
        await _publisher.DisposeAsync();
        await _connections.DisposeAsync();
        await _broker.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static DocumentIngestedEvent NewEvent() => new(
        MessageId: Guid.NewGuid(),
        DocumentId: Guid.NewGuid(),
        Type: DocumentType.NFe,
        IssuerCnpj: "11222333000181",
        IssuerState: "PE",
        IssueDate: new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero),
        TotalAmount: 1500.00m,
        OccurredAt: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(25);
        }
    }

    [Test]
    public async Task Consumer_TransientFailure_RetriesAndEventuallySucceeds()
    {
        // Arrange
        _processor.FailFirst(2);
        var @event = NewEvent();

        // Act
        await _publisher.PublishAsync(@event, CancellationToken.None);
        await WaitUntilAsync(() => _processor.SucceededIds.Contains(@event.MessageId));

        // Assert
        _processor.SucceededIds.Should().ContainSingle().Which.Should().Be(@event.MessageId);
        _processor.Attempts.Should().HaveCount(3);
        (await _broker.ReceiveDeadLettersAsync(1, TimeSpan.FromMilliseconds(500))).Should().BeEmpty();
    }

    [Test]
    public async Task Consumer_RetriedMessage_ArrivesWithTheSameEvent()
    {
        // Arrange
        _processor.FailFirst(1);
        var @event = NewEvent();

        // Act
        await _publisher.PublishAsync(@event, CancellationToken.None);
        await WaitUntilAsync(() => _processor.SucceededIds.Count == 1);

        // Assert
        _processor.Attempts.Select(a => a.MessageId).Should().OnlyContain(id => id == @event.MessageId);
        _processor.Attempts.Select(a => a.DocumentId).Should().OnlyContain(id => id == @event.DocumentId);
    }

    [Test]
    public async Task Consumer_Retry_WaitsForTheConfiguredDelayBeforeRedelivering()
    {
        // Arrange
        _processor.FailFirst(1);

        // Act
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await WaitUntilAsync(() => _processor.AttemptTimes.Count == 2);

        // Assert
        var gap = _processor.AttemptTimes[1] - _processor.AttemptTimes[0];
        gap.Should().BeGreaterThanOrEqualTo(InfrastructureFixture.RetryDelays[0] - TimeSpan.FromMilliseconds(50));
    }

    [Test]
    public async Task Consumer_Retry_UsesALongerDelayOnEachAttempt()
    {
        // Arrange
        _processor.FailFirst(3);

        // Act
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await WaitUntilAsync(() => _processor.AttemptTimes.Count == 4);

        // Assert
        var tolerance = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < InfrastructureFixture.RetryDelays.Count; i++)
        {
            var gap = _processor.AttemptTimes[i + 1] - _processor.AttemptTimes[i];
            gap.Should().BeGreaterThanOrEqualTo(InfrastructureFixture.RetryDelays[i] - tolerance);
        }
    }

    [Test]
    public async Task Consumer_PersistentFailure_MovesTheMessageToTheDeadLetterQueueAfterTheLastRetry()
    {
        // Arrange
        _processor.FailAlways();
        var @event = NewEvent();

        // Act
        await _publisher.PublishAsync(@event, CancellationToken.None);
        var dead = await _broker.ReceiveDeadLettersAsync(1, WaitTimeout);

        // Assert
        dead.Should().ContainSingle();
        dead[0].Properties.MessageId.Should().Be(@event.MessageId.ToString());
        dead[0].Body.Should().Contain(@event.MessageId.ToString());
        _processor.Attempts.Should().HaveCount(MaxDeliveries);
    }

    [Test]
    public async Task Consumer_AfterDeadLettering_LeavesNothingBehind()
    {
        // Arrange
        _processor.FailAlways();
        await _publisher.PublishAsync(NewEvent(), CancellationToken.None);
        await _broker.ReceiveDeadLettersAsync(1, WaitTimeout);

        // Act
        var main = await _broker.ReceiveAsync(1, TimeSpan.FromSeconds(1));
        var retrying = await _broker.CountRetryMessagesAsync();

        // Assert
        main.Should().BeEmpty();
        retrying.Should().Be(0);
        _processor.Attempts.Should().HaveCount(MaxDeliveries);
    }

    [Test]
    public async Task Consumer_MalformedMessage_GoesStraightToTheDeadLetterQueueWithoutRetry()
    {
        // Arrange
        const string body = "this is not json";

        // Act
        await _broker.PublishRawAsync(body);
        var dead = await _broker.ReceiveDeadLettersAsync(1, WaitTimeout);

        // Assert
        dead.Should().ContainSingle().Which.Body.Should().Be(body);
        _processor.Attempts.Should().BeEmpty();
        (await _broker.CountRetryMessagesAsync()).Should().Be(0);
    }

    [Test]
    public async Task Consumer_FailingMessage_DoesNotBlockOtherMessages()
    {
        // Arrange
        var poison = NewEvent();
        var healthy = NewEvent();
        _processor.FailAlwaysFor(poison.MessageId);

        // Act
        await _publisher.PublishAsync(poison, CancellationToken.None);
        await _publisher.PublishAsync(healthy, CancellationToken.None);
        await WaitUntilAsync(() => _processor.SucceededIds.Contains(healthy.MessageId));

        // Assert
        _processor.SucceededIds.Should().ContainSingle().Which.Should().Be(healthy.MessageId);
        _processor.Attempts.Count(a => a.MessageId == poison.MessageId).Should().BeLessThan(MaxDeliveries);
    }

    [Test]
    public async Task Consumer_SuccessOnFirstAttempt_DoesNotUseRetryOrDeadLetterQueues()
    {
        // Arrange
        var @event = NewEvent();

        // Act
        await _publisher.PublishAsync(@event, CancellationToken.None);
        await WaitUntilAsync(() => _processor.SucceededIds.Count == 1);

        // Assert
        _processor.Attempts.Should().HaveCount(1);
        (await _broker.CountRetryMessagesAsync()).Should().Be(0);
        (await _broker.ReceiveDeadLettersAsync(1, TimeSpan.FromMilliseconds(500))).Should().BeEmpty();
    }

    private sealed class ScriptedProcessor : IDocumentIngestedProcessor
    {
        private readonly object _gate = new();
        private readonly List<DocumentIngestedEvent> _attempts = [];
        private readonly List<DateTimeOffset> _attemptTimes = [];
        private readonly List<Guid> _succeeded = [];
        private int _failFirst;
        private bool _failAlways;
        private Guid? _failAlwaysFor;

        public IReadOnlyList<DocumentIngestedEvent> Attempts { get { lock (_gate) { return _attempts.ToList(); } } }

        public IReadOnlyList<DateTimeOffset> AttemptTimes { get { lock (_gate) { return _attemptTimes.ToList(); } } }

        public IReadOnlyList<Guid> SucceededIds { get { lock (_gate) { return _succeeded.ToList(); } } }

        public void FailFirst(int times) => _failFirst = times;

        public void FailAlways() => _failAlways = true;

        public void FailAlwaysFor(Guid messageId) => _failAlwaysFor = messageId;

        public Task ProcessAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _attempts.Add(@event);
                _attemptTimes.Add(DateTimeOffset.UtcNow);

                var shouldFail = _failAlways
                    || _failAlwaysFor == @event.MessageId
                    || _attempts.Count(a => a.MessageId == @event.MessageId) <= _failFirst;
                if (shouldFail)
                {
                    throw new InvalidOperationException("Simulated failure.");
                }

                _succeeded.Add(@event.MessageId);
            }

            return Task.CompletedTask;
        }
    }
}
