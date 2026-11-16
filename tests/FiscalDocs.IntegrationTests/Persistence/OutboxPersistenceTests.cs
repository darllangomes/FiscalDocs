using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Persistence;

[TestFixture]
public class OutboxPersistenceTests
{
    private const string NFeKey = "26260811222333000181550010000010011123456782";
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public async Task SetUp()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents, outbox_messages");
    }

    private static FiscalDocument Build(string key = NFeKey)
    {
        var accessKey = AccessKey.Parse(key);
        return FiscalDocument.Create(
            DocumentType.NFe, accessKey, "1001", "1",
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
            accessKey.IssuerCnpj, "EMPRESA FICTICIA LTDA", "PE",
            "12345678909", "CLIENTE FICTICIO", "SP",
            1500.00m, "<a/>", Now);
    }

    private static async Task<bool> TryAddAsync(FiscalDocument document)
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await new FiscalDocumentRepository(context).TryAddAsync(document, CancellationToken.None);
    }

    private static async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    [Test]
    public async Task TryAdd_NewDocument_StoresOnePendingOutboxMessage()
    {
        // Arrange
        var document = Build();

        // Act
        var added = await TryAddAsync(document);

        // Assert
        added.Should().BeTrue();
        var message = (await OutboxAsync()).Should().ContainSingle().Subject;
        message.Type.Should().Be("DocumentIngested");
        message.ProcessedAt.Should().BeNull();
    }

    [Test]
    public async Task TryAdd_NewDocument_StoresTheEventDescribingIt()
    {
        // Arrange
        var document = Build();

        // Act
        await TryAddAsync(document);

        // Assert
        var message = (await OutboxAsync()).Single();
        var @event = message.ToEvent();
        @event.MessageId.Should().Be(message.Id);
        @event.DocumentId.Should().Be(document.Id);
        @event.Type.Should().Be(DocumentType.NFe);
        @event.IssuerCnpj.Should().Be("11222333000181");
        @event.IssuerState.Should().Be("PE");
        @event.IssueDate.Should().Be(document.IssueDate);
        @event.TotalAmount.Should().Be(1500.00m);
        @event.OccurredAt.Should().Be(document.CreatedAt);
        message.OccurredAt.Should().Be(document.CreatedAt);
    }

    [Test]
    public async Task TryAdd_NewDocument_PayloadNeverCarriesRecipientDataOrXml()
    {
        // Act
        await TryAddAsync(Build());

        // Assert
        var payload = (await OutboxAsync()).Single().Payload;
        payload.Should().NotContain("12345678909");
        payload.Should().NotContain("CLIENTE FICTICIO");
        payload.Should().NotContain("<");
    }

    [Test]
    public async Task TryAdd_DuplicateAccessKey_StoresNoExtraMessage()
    {
        // Arrange
        await TryAddAsync(Build());

        // Act
        var added = await TryAddAsync(Build());

        // Assert
        added.Should().BeFalse();
        (await OutboxAsync()).Should().ContainSingle();
    }

    [Test]
    public async Task TryAdd_TwoDifferentDocuments_StoreTwoMessagesWithDifferentIds()
    {
        // Arrange
        await TryAddAsync(Build());

        // Act
        await TryAddAsync(Build("26260811222333000181550010000010021123456780"));

        // Assert
        (await OutboxAsync()).Select(m => m.Id).Distinct().Should().HaveCount(2);
    }
}
