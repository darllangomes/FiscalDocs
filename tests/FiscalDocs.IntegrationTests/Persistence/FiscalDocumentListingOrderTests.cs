using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using FiscalDocs.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Persistence;

[TestFixture]
public class FiscalDocumentListingOrderTests
{
    private static readonly string[] Keys =
    [
        "26260811222333000181550010000010011123456782",
        "26260811222333000181550010000010021123456780",
        "26260811222333000181550010000010031123456787",
        "26260811222333000181550010000010041123456784",
        "26260811222333000181550010000010051123456781"
    ];

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SameInstant = new(2026, 8, 14, 10, 30, 0, TimeSpan.Zero);

    [SetUp]
    public async Task SetUp()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents, outbox_messages");
    }

    private static FiscalDocument Build(string key, DateTimeOffset issueDate)
    {
        var accessKey = AccessKey.Parse(key);
        return FiscalDocument.Create(
            DocumentType.NFe, accessKey, "1001", "1", issueDate,
            accessKey.IssuerCnpj, "EMPRESA FICTICIA LTDA", "PE",
            null, null, null, 100.00m, "<a/>", Now);
    }

    private static async Task<List<FiscalDocument>> AddAsync(params DateTimeOffset[] issueDates)
    {
        var documents = issueDates.Select((date, i) => Build(Keys[i], date)).ToList();
        foreach (var document in documents)
        {
            await using var context = InfrastructureFixture.CreateContext();
            await new FiscalDocumentRepository(context).TryAddAsync(document, CancellationToken.None);
        }

        return documents;
    }

    private static async Task<PagedResult<DocumentSummary>> ListAsync(int page, int pageSize)
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await new FiscalDocumentRepository(context)
            .ListAsync(new ListDocumentsQuery(page, pageSize), CancellationToken.None);
    }

    [Test]
    public async Task List_SameIssueDate_OrdersByIdAscending()
    {
        // Arrange
        var documents = await AddAsync(SameInstant, SameInstant, SameInstant, SameInstant, SameInstant);

        // Act
        var result = await ListAsync(page: 1, pageSize: 10);

        // Assert
        result.Items.Select(i => i.Id).Should().Equal(documents.Select(d => d.Id).Order());
    }

    [Test]
    public async Task List_SameIssueDate_PagesNeverRepeatOrSkipADocument()
    {
        // Arrange
        var documents = await AddAsync(SameInstant, SameInstant, SameInstant, SameInstant, SameInstant);

        // Act
        var pages = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            pages.AddRange((await ListAsync(page, pageSize: 2)).Items.Select(i => i.Id));
        }

        // Assert
        pages.Should().Equal(documents.Select(d => d.Id).Order());
    }

    [Test]
    public async Task List_DifferentIssueDates_NewestFirstThenById()
    {
        // Arrange
        var older = SameInstant;
        var newer = SameInstant.AddDays(1);
        var documents = await AddAsync(older, newer, older, newer);

        // Act
        var result = await ListAsync(page: 1, pageSize: 10);

        // Assert
        var expected = documents.Where(d => d.IssueDate == newer).Select(d => d.Id).Order()
            .Concat(documents.Where(d => d.IssueDate == older).Select(d => d.Id).Order());
        result.Items.Select(i => i.Id).Should().Equal(expected);
    }
}
