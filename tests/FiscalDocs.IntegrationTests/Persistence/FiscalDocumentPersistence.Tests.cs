using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FiscalDocs.IntegrationTests.Persistence;

[TestFixture]
public class FiscalDocumentPersistenceTests
{
    private const string NFeKey = "26260811222333000181550010000010011123456782";
    private const string NFeAlphaKey = "26260812ABC34501DE35550010000020021234567895";
    private const string CTeKey = "35260844555666000181570010000030031345678900";
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static FiscalDocument Build(string key = NFeKey, DocumentType type = DocumentType.NFe)
    {
        var accessKey = AccessKey.Parse(key);
        return FiscalDocument.Create(
            type, accessKey, "1001", "1",
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
            accessKey.IssuerCnpj, "EMPRESA FICTICIA LTDA", "PE",
            "12345678909", "CLIENTE FICTICIO", "SP",
            1500.00m, "<a/>", Now);
    }

    private static async Task SaveAsync(params FiscalDocument[] documents)
    {
        await using var context = PostgresFixture.CreateContext();
        context.Documents.AddRange(documents);
        await context.SaveChangesAsync();
    }

    [SetUp]
    public async Task CleanTable()
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
    }

    [TestCase(NFeKey, DocumentType.NFe)]
    [TestCase(NFeAlphaKey, DocumentType.NFe)]
    [TestCase(CTeKey, DocumentType.CTe)]
    public async Task SaveAndLoad_RoundTripsAllFields(string key, DocumentType type)
    {
        var original = Build(key, type);
        await SaveAsync(original);

        await using var context = PostgresFixture.CreateContext();
        var loaded = await context.Documents.SingleAsync(d => d.Id == original.Id);

        loaded.Should().BeEquivalentTo(original, options => options
            .Using<DateTimeOffset>(c => c.Subject.Should().Be(c.Expectation))
            .WhenTypeIs<DateTimeOffset>());
    }

    [Test]
    public async Task SaveAndLoad_IssueDateIsStoredAsUtcInstant()
    {
        var original = Build(); // issued at -03:00
        await SaveAsync(original);

        await using var context = PostgresFixture.CreateContext();
        var loaded = await context.Documents.SingleAsync(d => d.Id == original.Id);

        loaded.IssueDate.Should().Be(original.IssueDate);
        loaded.IssueDate.Offset.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public async Task Save_SameAccessKeyTwice_ThrowsUniqueViolation()
    {
        await SaveAsync(Build());

        var act = () => SaveAsync(Build());

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task SoftDeletedDocument_IsHiddenByQueryFilter_ButVisibleWhenIgnoringIt()
    {
        var document = Build();
        document.MarkAsDeleted(Now.AddMinutes(1));
        await SaveAsync(document);

        await using var context = PostgresFixture.CreateContext();

        (await context.Documents.CountAsync()).Should().Be(0);
        (await context.Documents.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task Save_SameAccessKeyAsSoftDeletedDocument_StillViolatesUnique()
    {
        var deleted = Build();
        deleted.MarkAsDeleted(Now.AddMinutes(1));
        await SaveAsync(deleted);

        var act = () => SaveAsync(Build());

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task Query_ByIssuerCnpjAndDateRange_ReturnsOnlyMatches()
    {
        await SaveAsync(Build(NFeKey), Build(CTeKey, DocumentType.CTe));
        var issuer = Cnpj.Parse("11222333000181");
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        await using var context = PostgresFixture.CreateContext();
        var result = await context.Documents
            .Where(d => d.IssuerCnpj == issuer && d.IssueDate >= from && d.IssueDate < to)
            .ToListAsync();

        result.Should().ContainSingle().Which.AccessKey.Value.Should().Be(NFeKey);
    }

    [Test]
    public async Task UpdateManagement_IsPersisted()
    {
        var original = Build();
        await SaveAsync(original);

        await using (var context = PostgresFixture.CreateContext())
        {
            var tracked = await context.Documents.SingleAsync(d => d.Id == original.Id);
            tracked.UpdateManagement(DocumentStatus.Reviewed, "checked", Now.AddMinutes(5));
            await context.SaveChangesAsync();
        }

        await using var verify = PostgresFixture.CreateContext();
        var loaded = await verify.Documents.SingleAsync(d => d.Id == original.Id);
        loaded.Status.Should().Be(DocumentStatus.Reviewed);
        loaded.Notes.Should().Be("checked");
    }
}