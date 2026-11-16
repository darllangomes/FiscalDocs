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
public class OutboxCleanerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [SetUp]
    public async Task SetUp()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
    }

    // processedAt nulo = pendente.
    private static async Task<Guid> AddAsync(DateTimeOffset? processedAt, DateTimeOffset? occurredAt = null)
    {
        var @event = new DocumentIngestedEvent(
            MessageId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Type: DocumentType.NFe,
            IssuerDocument: "11222333000181",
            IssuerState: "PE",
            IssueDate: new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero),
            TotalAmount: 1500.00m,
            OccurredAt: occurredAt ?? Now.AddDays(-30));

        var message = OutboxMessage.From(@event);
        message.ProcessedAt = processedAt;

        await using var context = InfrastructureFixture.CreateContext();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        return message.Id;
    }

    private static async Task<Guid[]> RemainingAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.OutboxMessages.AsNoTracking().Select(m => m.Id).ToArrayAsync();
    }

    private static async Task<int> CleanAsync(int batchSize = 1000, TimeSpan? retention = null)
    {
        await using var context = InfrastructureFixture.CreateContext();
        var cleaner = new OutboxCleaner(context, new FixedClock(Now), NullLogger<OutboxCleaner>.Instance);
        return await cleaner.DeleteProcessedAsync(retention ?? Retention, batchSize, CancellationToken.None);
    }

    // ---------- O que é removido ----------

    [Test]
    public async Task ProcessedLongAgo_IsDeleted()
    {
        // Arrange
        await AddAsync(processedAt: Now - Retention - TimeSpan.FromMinutes(1));

        // Act
        var deleted = await CleanAsync();

        // Assert
        deleted.Should().Be(1);
        (await RemainingAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task ProcessedRecently_IsKept()
    {
        // Arrange
        var recent = await AddAsync(processedAt: Now - TimeSpan.FromDays(1));

        // Act
        var deleted = await CleanAsync();

        // Assert
        deleted.Should().Be(0);
        (await RemainingAsync()).Should().Equal(recent);
    }

    [Test]
    public async Task ProcessedExactlyAtTheRetentionLimit_IsKept()
    {
        // Arrange: só o que é estritamente mais antigo que o limite sai.
        var boundary = await AddAsync(processedAt: Now - Retention);

        // Act
        var deleted = await CleanAsync();

        // Assert
        deleted.Should().Be(0);
        (await RemainingAsync()).Should().Equal(boundary);
    }

    [Test]
    public async Task PendingMessage_IsNeverDeleted_EvenWhenVeryOld()
    {
        // Arrange: apagar uma pendente perderia o evento para sempre.
        var pending = await AddAsync(processedAt: null, occurredAt: Now.AddYears(-1));

        // Act
        var deleted = await CleanAsync();

        // Assert
        deleted.Should().Be(0);
        (await RemainingAsync()).Should().Equal(pending);
    }

    [Test]
    public async Task OnlyTheOldProcessedOnesGoAway_WhenTheTableIsMixed()
    {
        // Arrange
        await AddAsync(Now.AddDays(-30));
        await AddAsync(Now.AddDays(-8));
        var recent = await AddAsync(Now.AddDays(-2));
        var pending = await AddAsync(null);

        // Act
        var deleted = await CleanAsync();

        // Assert
        deleted.Should().Be(2);
        (await RemainingAsync()).Should().BeEquivalentTo(new[] { recent, pending });
    }

    // ---------- Lotes e repetição ----------

    [Test]
    public async Task MoreRowsThanTheBatch_AreAllDeleted_AndTheTotalIsReturned()
    {
        // Arrange
        for (var i = 0; i < 7; i++)
            await AddAsync(Now.AddDays(-10));

        // Act
        var deleted = await CleanAsync(batchSize: 3);

        // Assert
        deleted.Should().Be(7);
        (await RemainingAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task EmptyTable_DeletesNothing()
    {
        // Act & Assert
        (await CleanAsync()).Should().Be(0);
    }

    [Test]
    public async Task RunningTwice_DeletesNothingTheSecondTime()
    {
        // Arrange
        await AddAsync(Now.AddDays(-10));
        await CleanAsync();

        // Act & Assert
        (await CleanAsync()).Should().Be(0);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task InvalidBatchSize_Throws(int batchSize)
    {
        // Act
        var act = () => CleanAsync(batchSize: batchSize);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ZeroRetention_Throws_BecauseItWouldEmptyTheOutboxRightAway()
    {
        // Act
        var act = () => CleanAsync(retention: TimeSpan.Zero);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ---------- Índice ----------

    [Test]
    public async Task ThereIsAPartialIndexOnProcessedAt_SoTheCleanupDoesNotScanTheTable()
    {
        // Arrange
        await using var context = InfrastructureFixture.CreateContext();

        // Act
        var definition = await context.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'IX_outbox_messages_processed'")
            .SingleOrDefaultAsync();

        // Assert
        definition.Should().NotBeNull();
        definition.Should().Contain("processed_at").And.Contain("IS NOT NULL");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

[TestFixture]
public class OutboxCleanupLoopTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

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

        _services = new ServiceCollection()
            .AddDbContext<FiscalDocsDbContext>(o => o.UseNpgsql(InfrastructureFixture.ConnectionString))
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddScoped<OutboxCleaner>()
            .BuildServiceProvider();
        _cts = new CancellationTokenSource();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _cts.CancelAsync();
        if (_running is not null)
            await _running.WaitAsync(TimeSpan.FromSeconds(5));

        _cts.Dispose();
        await _services.DisposeAsync();
    }

    private void Start() =>
        _running = new OutboxCleanup(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new OutboxCleanupOptions(
                Retention: TimeSpan.FromDays(7), Interval: TimeSpan.FromMilliseconds(100), BatchSize: 100),
            NullLogger<OutboxCleanup>.Instance).RunAsync(_cts.Token);

    private static async Task AddProcessedAsync(DateTimeOffset processedAt)
    {
        var @event = new DocumentIngestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), DocumentType.NFe, "11222333000181", "PE",
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero), 1500.00m, processedAt.AddMinutes(-1));
        var message = OutboxMessage.From(@event);
        message.ProcessedAt = processedAt;

        await using var context = InfrastructureFixture.CreateContext();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
    }

    private static async Task<int> CountAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.OutboxMessages.CountAsync();
    }

    private static async Task WaitUntilCountAsync(int expected)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (await CountAsync() != expected)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {expected} outbox row(s); {await CountAsync()} left.");
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task Loop_DeletesOldProcessedRows_AndKeepsDoingSoOnLaterIterations()
    {
        // Arrange
        await AddProcessedAsync(DateTimeOffset.UtcNow.AddDays(-10));
        Start();
        await WaitUntilCountAsync(0);

        // Act: uma linha que envelhece depois da primeira varredura também precisa sair.
        await AddProcessedAsync(DateTimeOffset.UtcNow.AddDays(-9));

        // Assert
        await WaitUntilCountAsync(0);
    }

    [Test]
    public async Task Loop_StopsCleanlyWhenCancelled()
    {
        // Arrange
        Start();
        await Task.Delay(300);

        // Act
        await _cts.CancelAsync();

        // Assert
        await _running!.WaitAsync(TimeSpan.FromSeconds(5));
        _running.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Test]
    public void Options_RejectNonPositiveValues()
    {
        // Act & Assert
        var ok = TimeSpan.FromMinutes(1);
        new Action(() => _ = new OutboxCleanupOptions(TimeSpan.Zero, ok, 10)).Should().Throw<ArgumentOutOfRangeException>();
        new Action(() => _ = new OutboxCleanupOptions(ok, TimeSpan.Zero, 10)).Should().Throw<ArgumentOutOfRangeException>();
        new Action(() => _ = new OutboxCleanupOptions(ok, ok, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
