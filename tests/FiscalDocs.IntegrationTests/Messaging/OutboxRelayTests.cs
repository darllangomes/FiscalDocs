using FiscalDocs.Application.Messaging;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class OutboxRelayTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private OutboxDispatcherTests.RecordingPublisher _publisher = null!;
    private ServiceProvider _services = null!;
    private CancellationTokenSource _cts = null!;
    private Task? _running;

    [SetUp]
    public async Task SetUp()
    {
        await using (var context = InfrastructureFixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
        }

        _publisher = new OutboxDispatcherTests.RecordingPublisher();
        _services = new ServiceCollection()
            .AddDbContext<FiscalDocsDbContext>(o => o.UseNpgsql(InfrastructureFixture.ConnectionString))
            .AddSingleton(TimeProvider.System)
            .AddSingleton<FiscalDocs.Application.Abstractions.IDocumentEventPublisher>(_publisher)
            .AddLogging()
            .AddScoped<OutboxDispatcher>()
            .BuildServiceProvider();
        _cts = new CancellationTokenSource();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _cts.CancelAsync();
        if (_running is not null)
        {
            await _running.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _cts.Dispose();
        await _services.DisposeAsync();
    }

    private void StartRelay() =>
        _running = new OutboxRelay(
            _services.GetRequiredService<IServiceScopeFactory>(),
            PollInterval,
            NullLogger<OutboxRelay>.Instance).RunAsync(_cts.Token);

    private static DocumentIngestedEvent NewEvent() => new(
        MessageId: Guid.NewGuid(),
        DocumentId: Guid.NewGuid(),
        Type: DocumentType.NFe,
        IssuerCnpj: "11222333000181",
        IssuerState: "PE",
        IssueDate: new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero),
        TotalAmount: 1500.00m,
        OccurredAt: DateTimeOffset.UtcNow);

    private static async Task AddPendingAsync(DocumentIngestedEvent @event)
    {
        await using var context = InfrastructureFixture.CreateContext();
        context.OutboxMessages.Add(OutboxMessage.From(@event));
        await context.SaveChangesAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(25);
        }
    }

    [Test]
    public async Task Relay_PublishesMessagesAlreadyWaitingWhenItStarts()
    {
        // Arrange
        var waiting = NewEvent();
        await AddPendingAsync(waiting);

        // Act
        StartRelay();
        await WaitUntilAsync(() => _publisher.Published.Count == 1);

        // Assert
        _publisher.Published.Should().ContainSingle().Which.MessageId.Should().Be(waiting.MessageId);
    }

    [Test]
    public async Task Relay_PublishesMessagesInsertedWhileItIsRunning()
    {
        // Arrange
        StartRelay();
        await Task.Delay(PollInterval * 2);
        var late = NewEvent();

        // Act
        await AddPendingAsync(late);
        await WaitUntilAsync(() => _publisher.Published.Count == 1);

        // Assert
        _publisher.Published.Should().ContainSingle().Which.MessageId.Should().Be(late.MessageId);
    }

    [Test]
    public async Task Relay_WhenThePublisherFailsAtFirst_KeepsRunningAndDeliversLater()
    {
        // Arrange
        var flaky = NewEvent();
        _publisher.FailOn = flaky.MessageId;
        await AddPendingAsync(flaky);
        StartRelay();
        await Task.Delay(PollInterval * 3);

        // Act
        _publisher.FailOn = null;
        await WaitUntilAsync(() => _publisher.Published.Count == 1);

        // Assert
        _publisher.Published.Should().ContainSingle().Which.MessageId.Should().Be(flaky.MessageId);
    }

    [Test]
    public async Task Relay_WhenCancelled_StopsPromptly()
    {
        // Arrange
        StartRelay();
        await Task.Delay(PollInterval * 2);

        // Act
        await _cts.CancelAsync();

        // Assert
        (await Task.WhenAny(_running!, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(_running);
    }
}
