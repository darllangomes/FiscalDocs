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
            accessKey.IssuerCnpj, "EMPRESA FICTICIA LTDA", "PE",
            "12345678909", "CLIENTE FICTICIO", "SP",
            1500.00m, "<a/>", Now);
    }

    // Each call uses its own context, like separate HTTP requests would.
    private static async Task<bool> TryAddAsync(FiscalDocument document)
    {
        await using var context = PostgresFixture.CreateContext();
        return await new FiscalDocumentRepository(context).TryAddAsync(document, CancellationToken.None);
    }

    private static async Task<FiscalDocument?> FindAsync(string key)
    {
        await using var context = PostgresFixture.CreateContext();
        return await new FiscalDocumentRepository(context)
            .FindByAccessKeyAsync(AccessKey.Parse(key), CancellationToken.None);
    }

    private static async Task<int> CountAllAsync()
    {
        await using var context = PostgresFixture.CreateContext();
        return await context.Documents.IgnoreQueryFilters().CountAsync();
    }

    [SetUp]
    public async Task CleanTable()
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
    }

    // ---------- TryAddAsync ----------

    [Test]
    public async Task TryAdd_NewDocument_ReturnsTrueAndPersists()
    {
        (await TryAddAsync(Build())).Should().BeTrue();

        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task TryAdd_SameAccessKeyFromAnotherContext_ReturnsFalse()
    {
        await TryAddAsync(Build());

        (await TryAddAsync(Build())).Should().BeFalse();

        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task TryAdd_DuplicateInSameContext_ReturnsFalseAndContextStaysUsable()
    {
        await using var context = PostgresFixture.CreateContext();
        var repository = new FiscalDocumentRepository(context);
        var first = Build();
        await repository.TryAddAsync(first, CancellationToken.None);

        var added = await repository.TryAddAsync(Build(), CancellationToken.None);
        var found = await repository.FindByAccessKeyAsync(first.AccessKey, CancellationToken.None);

        added.Should().BeFalse();
        found.Should().NotBeNull();
        found!.Id.Should().Be(first.Id);
        context.ChangeTracker.Entries<FiscalDocument>()
            .Should().NotContain(e => e.State == EntityState.Added);
    }

    [Test]
    public async Task TryAdd_ConcurrentUploadsOfSameKey_ExactlyOneSucceeds()
    {
        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => Task.Run(() => TryAddAsync(Build()))));

        results.Count(succeeded => succeeded).Should().Be(1);
        (await CountAllAsync()).Should().Be(1);
    }

    // ---------- FindByAccessKeyAsync ----------

    [Test]
    public async Task Find_ExistingDocument_ReturnsIt()
    {
        var document = Build();
        await TryAddAsync(document);

        var found = await FindAsync(NFeKey);

        found.Should().NotBeNull();
        found!.Id.Should().Be(document.Id);
    }

    [Test]
    public async Task Find_SoftDeletedDocument_StillReturnsIt()
    {
        var document = Build();
        document.MarkAsDeleted(Now.AddMinutes(1));
        await TryAddAsync(document);

        var found = await FindAsync(NFeKey);

        found.Should().NotBeNull();
        found!.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task Find_UnknownKey_ReturnsNull()
    {
        (await FindAsync(NFeKey)).Should().BeNull();
    }
}
