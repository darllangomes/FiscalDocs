using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using FiscalDocs.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Persistence;

[TestFixture]
public class FiscalDocumentRepositoryTests
{
    private const string NFeKey = "26260811222333000181550010000010011123456782";
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static FiscalDocument Build(string key = NFeKey)
    {
        var accessKey = AccessKey.Parse(key);
        return FiscalDocument.Create(
            DocumentType.NFe, accessKey, "1001", "1",
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
            TaxId.Parse(accessKey.Value[6..20]), "EMPRESA FICTICIA LTDA", "PE",
            "12345678909", "CLIENTE FICTICIO", "SP",
            1500.00m, "<a/>", Now);
    }

    private static async Task<bool> TryAddAsync(FiscalDocument document)
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await new FiscalDocumentRepository(context).TryAddAsync(document, CancellationToken.None);
    }

    private static async Task<FiscalDocument?> FindAsync(string key)
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await new FiscalDocumentRepository(context)
            .FindByAccessKeyAsync(AccessKey.Parse(key), CancellationToken.None);
    }

    private static async Task<int> CountAllAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.Documents.IgnoreQueryFilters().CountAsync();
    }

    [SetUp]
    public async Task CleanTable()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
    }

    // ---------- TryAddAsync ----------

    [Test]
    public async Task TryAdd_NewDocument_ReturnsTrueAndPersists()
    {
        // Act & Assert
        (await TryAddAsync(Build())).Should().BeTrue();
        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task TryAdd_SameAccessKeyFromAnotherContext_ReturnsFalse()
    {
        // Arrange
        await TryAddAsync(Build());

        // Act
        var added = await TryAddAsync(Build());

        // Assert
        added.Should().BeFalse();
        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task TryAdd_DuplicateInSameContext_ReturnsFalseAndContextStaysUsable()
    {
        // Arrange
        await using var context = InfrastructureFixture.CreateContext();
        var repository = new FiscalDocumentRepository(context);
        var first = Build();
        await repository.TryAddAsync(first, CancellationToken.None);

        // Act
        var added = await repository.TryAddAsync(Build(), CancellationToken.None);
        var found = await repository.FindByAccessKeyAsync(first.AccessKey, CancellationToken.None);

        // Assert
        added.Should().BeFalse();
        found.Should().NotBeNull();
        found!.Id.Should().Be(first.Id);
        context.ChangeTracker.Entries<FiscalDocument>()
            .Should().NotContain(e => e.State == EntityState.Added);
    }

    [Test]
    public async Task TryAdd_ConcurrentUploadsOfSameKey_ExactlyOneSucceeds()
    {
        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => Task.Run(() => TryAddAsync(Build()))));

        // Assert
        results.Count(succeeded => succeeded).Should().Be(1);
        (await CountAllAsync()).Should().Be(1);
    }

    // ---------- FindByAccessKeyAsync ----------

    [Test]
    public async Task Find_ExistingDocument_ReturnsIt()
    {
        // Arrange
        var document = Build();
        await TryAddAsync(document);

        // Act
        var found = await FindAsync(NFeKey);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(document.Id);
    }

    [Test]
    public async Task Find_SoftDeletedDocument_StillReturnsIt()
    {
        // Arrange
        var document = Build();
        document.MarkAsDeleted(Now.AddMinutes(1));
        await TryAddAsync(document);

        // Act
        var found = await FindAsync(NFeKey);

        // Assert
        found.Should().NotBeNull();
        found!.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task Find_UnknownKey_ReturnsNull()
    {
        // Act & Assert
        (await FindAsync(NFeKey)).Should().BeNull();
    }

    // ---------- FindXmlByIdAsync ----------

    private static async Task<DocumentXml?> FindXmlAsync(Guid id)
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await new FiscalDocumentRepository(context).FindXmlByIdAsync(id, CancellationToken.None);
    }

    [Test]
    public async Task FindXml_ExistingDocument_ReturnsTheStoredXmlAndTheAccessKey()
    {
        // Arrange
        var document = Build();
        await TryAddAsync(document);

        // Act
        var xml = await FindXmlAsync(document.Id);

        // Assert
        xml.Should().NotBeNull();
        xml!.Xml.Should().Be("<a/>");
        xml.AccessKey.Should().Be(NFeKey);
    }

    [Test]
    public async Task FindXml_UnknownId_ReturnsNull()
    {
        // Act & Assert
        (await FindXmlAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Test]
    public async Task FindXml_SoftDeletedDocument_ReturnsNull()
    {
        // Arrange
        var document = Build();
        document.MarkAsDeleted(Now.AddMinutes(1));
        await TryAddAsync(document);

        // Act & Assert
        (await FindXmlAsync(document.Id)).Should().BeNull();
    }
}
