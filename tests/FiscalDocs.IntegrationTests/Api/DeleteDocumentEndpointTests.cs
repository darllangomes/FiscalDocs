using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class DeleteDocumentEndpointTests
{
    private const string Route = "/api/v1/documents";

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
    public async Task CleanTable()
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
    }

    private async Task<Guid> IngestSampleAsync(string file = "nfe-cnpj-numerico.xml")
    {
        var content = new StringContent(SampleFiles.ReadAllText(file), Encoding.UTF8, "application/xml");
        var response = await _client.PostAsync(Route, content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<string> GetEtagAsync(Guid id)
    {
        var response = await _client.GetAsync($"{Route}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response.Headers.ETag!.ToString();
    }

    private Task<HttpResponseMessage> DeleteAsync(Guid id, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{Route}/{id}");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return _client.SendAsync(request);
    }

    // ---------- sucesso ----------

    [Test]
    public async Task ExistingDocument_Returns204WithNoBody()
    {
        var id = await IngestSampleAsync();

        var response = await DeleteAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task DeletedDocument_NoLongerAppearsInGet()
    {
        var id = await IngestSampleAsync();
        (await DeleteAsync(id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeletedDocument_NoLongerAppearsInList()
    {
        var id = await IngestSampleAsync();
        await IngestSampleAsync("nfe-cnpj-alfanumerico.xml");
        (await DeleteAsync(id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync(Route);

        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.GetProperty("totalCount").GetInt32().Should().Be(1);
        list.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())
            .Should().NotContain(id);
    }

    [Test]
    public async Task Delete_IsSoft_RowAndRawXmlRemainInDatabase()
    {
        var id = await IngestSampleAsync();
        (await DeleteAsync(id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var context = PostgresFixture.CreateContext();
        var document = await context.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == id);
        document.IsDeleted.Should().BeTrue();
        document.RawXml.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Delete_DoesNotAffectOtherDocuments()
    {
        var target = await IngestSampleAsync("nfe-cnpj-numerico.xml");
        var other = await IngestSampleAsync("nfe-cnpj-alfanumerico.xml");

        (await DeleteAsync(target)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"{Route}/{other}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- repetição e efeitos colaterais ----------

    [Test]
    public async Task SecondDelete_Returns404()
    {
        var id = await IngestSampleAsync();
        (await DeleteAsync(id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await DeleteAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ReingestingDeletedDocument_Returns409()
    {
        var id = await IngestSampleAsync();
        (await DeleteAsync(id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var content = new StringContent(
            SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), Encoding.UTF8, "application/xml");
        var response = await _client.PostAsync(Route, content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task PatchAfterDelete_Returns404()
    {
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);
        (await DeleteAsync(id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"{Route}/{id}")
        {
            Content = new StringContent("""{"status":"Reviewed"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- If-Match opcional ----------

    [Test]
    public async Task MatchingIfMatch_Returns204()
    {
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);

        var response = await DeleteAsync(id, etag);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task StaleIfMatch_Returns412_AndKeepsDocument()
    {
        var id = await IngestSampleAsync();
        var stale = await GetEtagAsync(id);
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Route}/{id}")
        {
            Content = new StringContent("""{"notes":"mudou"}""", Encoding.UTF8, "application/json")
        };
        patch.Headers.TryAddWithoutValidation("If-Match", stale);
        (await _client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await DeleteAsync(id, stale);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await _client.GetAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task WeakIfMatch_Returns412_AndKeepsDocument()
    {
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);

        var response = await DeleteAsync(id, $"W/{etag}");

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await _client.GetAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestCase("\"nao-e-um-xmin\"")]
    [TestCase("\"99999999999999999999\"")]
    [TestCase("sem-aspas")]
    [TestCase("*")]
    public async Task GarbageIfMatch_Returns412_AndKeepsDocument(string ifMatch)
    {
        var id = await IngestSampleAsync();

        var response = await DeleteAsync(id, ifMatch);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await _client.GetAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- 404 / 401 ----------

    [Test]
    public async Task UnknownId_Returns404ProblemDetails()
    {
        var response = await DeleteAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public async Task UnknownId_Returns404_EvenWithIfMatch()
    {
        var response = await DeleteAsync(Guid.NewGuid(), "\"123\"");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestCase("not-a-guid")]
    [TestCase("123")]
    public async Task MalformedId_Returns404(string id)
    {
        var response = await _client.DeleteAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task WithoutApiKey_Returns401_AndKeepsDocument()
    {
        var id = await IngestSampleAsync();
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.DeleteAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
