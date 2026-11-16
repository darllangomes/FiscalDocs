using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.Enums;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class ListDocumentsEndpointTests
{
    private const string Route = "/api/v1/documents";

    private static readonly string[] Samples =
    {
        "nfe-cnpj-numerico.xml",      // 1001, NFe, PE, 14/08
        "nfe-cnpj-alfanumerico.xml",  // 2002, NFe, PE, 20/08
        "cte-cnpj-numerico.xml",      // 3003, CTe, SP, 15/08
        "cte-cnpj-alfanumerico.xml",  // 4004, CTe, SP, 22/08
    };

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateAuthenticatedClient();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public async Task SeedAsync()
    {
        await using (var context = InfrastructureFixture.CreateContext())
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");

        foreach (var file in Samples)
        {
            var content = new StringContent(SampleFiles.ReadAllText(file), Encoding.UTF8, "application/xml");
            var response = await _client.PostAsync(Route, content);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    private async Task<JsonElement> ListAsync(string query = "")
    {
        var response = await _client.GetAsync(query.Length == 0 ? Route : $"{Route}?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] NumbersOf(JsonElement list) =>
        list.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("number").GetString()!)
            .ToArray();

    private static async Task ModifyAsync(string number, Action<FiscalDocument> change)
    {
        await using var context = InfrastructureFixture.CreateContext();
        var document = await context.Documents.SingleAsync(d => d.Number == number);
        change(document);
        await context.SaveChangesAsync();
    }

    // ---------- Shape, ordering, paging ----------

    [Test]
    public async Task Default_ReturnsEverythingNewestFirstWithPagingMetadata()
    {
        // Act
        var list = await ListAsync();

        // Assert
        NumbersOf(list).Should().Equal("4004", "2002", "3003", "1001");
        list.GetProperty("page").GetInt32().Should().Be(1);
        list.GetProperty("pageSize").GetInt32().Should().Be(20);
        list.GetProperty("totalCount").GetInt32().Should().Be(4);
    }

    [Test]
    public async Task Items_AreSummaries_WithoutRawXmlOrNotes()
    {
        // Act
        var list = await ListAsync();

        // Assert
        var first = list.GetProperty("items")[0];
        first.GetProperty("id").GetGuid().Should().NotBeEmpty();
        first.GetProperty("type").GetString().Should().Be("CTe");
        first.GetProperty("status").GetString().Should().Be("Received");
        first.GetProperty("issuerCnpj").GetString().Should().Be("7K9X2M4P000144");
        first.GetProperty("issuerState").GetString().Should().Be("SP");
        first.TryGetProperty("rawXml", out _).Should().BeFalse();
        first.TryGetProperty("notes", out _).Should().BeFalse();
    }

    [Test]
    public async Task Paging_ReturnsRequestedSlice_AndKeepsTotalCount()
    {
        // Act
        var list = await ListAsync("page=2&pageSize=2");

        // Assert
        NumbersOf(list).Should().Equal("3003", "1001");
        list.GetProperty("page").GetInt32().Should().Be(2);
        list.GetProperty("pageSize").GetInt32().Should().Be(2);
        list.GetProperty("totalCount").GetInt32().Should().Be(4);
    }

    [Test]
    public async Task PageBeyondTheEnd_Returns200WithEmptyItems()
    {
        // Act
        var list = await ListAsync("page=9&pageSize=2");

        // Assert
        NumbersOf(list).Should().BeEmpty();
        list.GetProperty("totalCount").GetInt32().Should().Be(4);
    }

    [Test]
    public async Task WalkingPagesOfOne_VisitsEveryDocumentOnceInOrder()
    {
        // Arrange
        var visited = new List<string>();

        // Act
        for (var page = 1; page <= 4; page++)
            visited.AddRange(NumbersOf(await ListAsync($"page={page}&pageSize=1")));

        // Assert
        visited.Should().Equal("4004", "2002", "3003", "1001");
    }

    // ---------- Filters ----------

    [TestCase("type=CTe", new[] { "4004", "3003" })]
    [TestCase("type=NFe", new[] { "2002", "1001" })]
    [TestCase("issuerState=SP", new[] { "4004", "3003" })]
    [TestCase("issuerState=pe", new[] { "2002", "1001" })]                       // case-insensitive
    [TestCase("issuerCnpj=12ABC34501DE35", new[] { "2002" })]
    [TestCase("issuerCnpj=12.abc.345/01de-35", new[] { "2002" })]                // same normalization as Cnpj.TryParse
    [TestCase("issuedFrom=2026-08-15&issuedTo=2026-08-20", new[] { "2002", "3003" })] // both bounds inclusive
    [TestCase("issuedFrom=2026-08-21", new[] { "4004" })]
    [TestCase("issuedTo=2026-08-14", new[] { "1001" })]
    [TestCase("type=NFe&issuerState=PE&issuedFrom=2026-08-15", new[] { "2002" })] // filters combine with AND
    [TestCase("issuerState=RS", new string[0])]
    public async Task Filters_ReturnOnlyMatchingDocuments(string query, string[] expected)
    {
        // Act
        var list = await ListAsync(query);

        // Assert
        NumbersOf(list).Should().Equal(expected);
        list.GetProperty("totalCount").GetInt32().Should().Be(expected.Length);
    }

    [Test]
    public async Task StatusFilter_ReturnsOnlyDocumentsInThatStatus()
    {
        // Arrange
        await ModifyAsync("1001", d => d.UpdateManagement(DocumentStatus.Reviewed, "checked", DateTimeOffset.UtcNow));

        // Act
        var reviewed = await ListAsync("status=Reviewed");
        var received = await ListAsync("status=Received");

        // Assert
        NumbersOf(reviewed).Should().Equal("1001");
        NumbersOf(received).Should().Equal("4004", "2002", "3003");
    }

    [Test]
    public async Task SoftDeletedDocuments_AreExcludedFromItemsAndTotal()
    {
        // Arrange
        await ModifyAsync("3003", d => d.MarkAsDeleted(DateTimeOffset.UtcNow));

        // Act
        var list = await ListAsync();

        // Assert
        NumbersOf(list).Should().Equal("4004", "2002", "1001");
        list.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    // ---------- Validation ----------

    [TestCase("page=0")]
    [TestCase("page=-1")]
    [TestCase("page=abc")]
    [TestCase("pageSize=0")]
    [TestCase("pageSize=101")]
    [TestCase("type=XYZ")]
    [TestCase("status=Foo")]
    [TestCase("issuedFrom=not-a-date")]
    [TestCase("issuerCnpj=123")]
    [TestCase("issuerState=SPX")]
    [TestCase("issuedFrom=2026-08-20&issuedTo=2026-08-10")]
    public async Task InvalidQuery_Returns400ProblemDetails(string query)
    {
        // Act
        var response = await _client.GetAsync($"{Route}?{query}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // ---------- Security ----------

    [Test]
    public async Task WithoutApiKey_Returns401()
    {
        // Arrange
        using var anonymous = _factory.CreateClient();

        // Act
        var response = await anonymous.GetAsync(Route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
