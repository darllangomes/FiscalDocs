using FiscalDocs.Application.Messaging;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class DocumentIngestedProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public async Task SetUp()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE daily_issuer_aggregates, processed_messages");
    }

    private static DocumentIngestedEvent NewEvent(
        string cnpj = "11222333000181",
        string state = "PE",
        DateTimeOffset? issueDate = null,
        decimal total = 1500.00m,
        Guid? messageId = null) => new(
            MessageId: messageId ?? Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Type: DocumentType.NFe,
            IssuerDocument: cnpj,
            IssuerState: state,
            IssueDate: issueDate ?? new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero),
            TotalAmount: total,
            OccurredAt: Now);

    private static async Task ProcessAsync(DocumentIngestedEvent @event)
    {
        await using var context = InfrastructureFixture.CreateContext();
        var processor = new DocumentIngestedProcessor(context, new FakeClock(Now));
        await processor.ProcessAsync(@event, CancellationToken.None);
    }

    private static async Task<List<DailyIssuerAggregate>> AggregatesAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.DailyAggregates.AsNoTracking().ToListAsync();
    }

    private static async Task<int> ProcessedCountAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.ProcessedMessages.CountAsync();
    }

    [Test]
    public async Task Process_FirstEventOfTheDay_CreatesTheAggregate()
    {
        // Arrange
        var @event = NewEvent(total: 1500.00m);

        // Act
        await ProcessAsync(@event);

        // Assert
        var aggregate = (await AggregatesAsync()).Should().ContainSingle().Subject;
        aggregate.Date.Should().Be(new DateOnly(2026, 8, 14));
        aggregate.IssuerDocument.Should().Be("11222333000181");
        aggregate.IssuerState.Should().Be("PE");
        aggregate.DocumentCount.Should().Be(1);
        aggregate.TotalAmount.Should().Be(1500.00m);
    }

    [Test]
    public async Task Process_SecondEventSameIssuerAndDay_AddsToTheExistingAggregate()
    {
        // Arrange
        await ProcessAsync(NewEvent(total: 1500.00m));

        // Act
        await ProcessAsync(NewEvent(total: 250.50m));

        // Assert
        var aggregate = (await AggregatesAsync()).Should().ContainSingle().Subject;
        aggregate.DocumentCount.Should().Be(2);
        aggregate.TotalAmount.Should().Be(1750.50m);
    }

    [Test]
    public async Task Process_SameMessageIdTwice_CountsItOnce()
    {
        // Arrange
        var @event = NewEvent(total: 1500.00m);
        await ProcessAsync(@event);

        // Act
        await ProcessAsync(@event);

        // Assert
        var aggregate = (await AggregatesAsync()).Should().ContainSingle().Subject;
        aggregate.DocumentCount.Should().Be(1);
        aggregate.TotalAmount.Should().Be(1500.00m);
        (await ProcessedCountAsync()).Should().Be(1);
    }

    [Test]
    public async Task Process_SameMessageIdConcurrently_CountsItOnce()
    {
        // Arrange
        var @event = NewEvent(total: 1500.00m);

        // Act
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ProcessAsync(@event)));

        // Assert
        var aggregate = (await AggregatesAsync()).Should().ContainSingle().Subject;
        aggregate.DocumentCount.Should().Be(1);
        aggregate.TotalAmount.Should().Be(1500.00m);
    }

    [Test]
    public async Task Process_DifferentEventsConcurrentlyForTheSameBucket_LosesNoUpdate()
    {
        // Arrange
        var events = Enumerable.Range(0, 8).Select(_ => NewEvent(total: 100.00m)).ToList();

        // Act
        await Task.WhenAll(events.Select(ProcessAsync));

        // Assert
        var aggregate = (await AggregatesAsync()).Should().ContainSingle().Subject;
        aggregate.DocumentCount.Should().Be(8);
        aggregate.TotalAmount.Should().Be(800.00m);
    }

    [Test]
    public async Task Process_DifferentIssuer_CreatesASeparateAggregate()
    {
        // Arrange
        await ProcessAsync(NewEvent(cnpj: "11222333000181"));

        // Act
        await ProcessAsync(NewEvent(cnpj: "44555666000181", state: "SP"));

        // Assert
        (await AggregatesAsync()).Should().HaveCount(2);
    }

    [Test]
    public async Task Process_DifferentDay_CreatesASeparateAggregate()
    {
        // Arrange
        await ProcessAsync(NewEvent(issueDate: new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero)));

        // Act
        await ProcessAsync(NewEvent(issueDate: new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero)));

        // Assert
        (await AggregatesAsync()).Select(a => a.Date).Should()
            .BeEquivalentTo(new[] { new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 15) });
    }

    [Test]
    public async Task Process_IssueDateNearMidnightWithOffset_BucketsByTheUtcDay()
    {
        // Arrange
        var issuedAt = new DateTimeOffset(2026, 8, 14, 23, 30, 0, TimeSpan.FromHours(-3));

        // Act
        await ProcessAsync(NewEvent(issueDate: issuedAt));

        // Assert
        (await AggregatesAsync()).Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2026, 8, 15));
    }

    [Test]
    public async Task Process_RecordsTheMessageAsProcessed()
    {
        // Arrange
        var @event = NewEvent();

        // Act
        await ProcessAsync(@event);

        // Assert
        await using var context = InfrastructureFixture.CreateContext();
        var processed = await context.ProcessedMessages.SingleAsync();
        processed.MessageId.Should().Be(@event.MessageId);
        processed.ProcessedAt.Should().Be(Now);
    }

    [Test]
    public async Task Process_WhenTheAggregateWriteFails_DoesNotMarkTheMessageAsProcessed()
    {
        // Arrange
        var invalid = NewEvent(state: new string('X', 50));

        // Act
        var act = () => ProcessAsync(invalid);

        // Assert
        await act.Should().ThrowAsync<Exception>();
        (await ProcessedCountAsync()).Should().Be(0);
        (await AggregatesAsync()).Should().BeEmpty();
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
