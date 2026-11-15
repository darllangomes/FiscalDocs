using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class PatchDocumentEndpointTests
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

    private Task<HttpResponseMessage> PatchAsync(
        Guid id, string json, string? ifMatch, string mediaType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{Route}/{id}")
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType)
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return _client.SendAsync(request);
    }

    private async Task<JsonElement> GetBodyAsync(Guid id)
    {
        var response = await _client.GetAsync($"{Route}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---------- sucesso ----------

    [Test]
    public async Task StatusAndNotes_Returns200WithUpdatedDocumentAndNewEtag()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);

        // Act
        var response = await PatchAsync(id, """{"status":"Reviewed","notes":"conferida"}""", etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("id").GetGuid().Should().Be(id);
        doc.GetProperty("status").GetString().Should().Be("Reviewed");
        doc.GetProperty("notes").GetString().Should().Be("conferida");
        doc.GetProperty("number").GetString().Should().Be("1001");
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.IsWeak.Should().BeFalse();
        response.Headers.ETag.ToString().Should().NotBe(etag);
    }

    [Test]
    public async Task Update_IsPersisted_AndGetReturnsSameNewEtag()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);
        var patch = await PatchAsync(id, """{"status":"Archived","notes":"fechada"}""", etag);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var get = await _client.GetAsync($"{Route}/{id}");

        // Assert
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Headers.ETag.Should().Be(patch.Headers.ETag);
        var doc = await get.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Archived");
        doc.GetProperty("notes").GetString().Should().Be("fechada");
    }

    [Test]
    public async Task Update_AdvancesUpdatedAt_AndKeepsCreatedAt()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var before = await GetBodyAsync(id);
        var etag = await GetEtagAsync(id);

        // Act
        var response = await PatchAsync(id, """{"notes":"x"}""", etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await response.Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("createdAt").GetDateTimeOffset()
            .Should().Be(before.GetProperty("createdAt").GetDateTimeOffset());
        after.GetProperty("updatedAt").GetDateTimeOffset()
            .Should().BeAfter(before.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Test]
    public async Task OnlyStatus_LeavesNotesUnchanged()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var first = await PatchAsync(id, """{"notes":"original"}""", await GetEtagAsync(id));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await PatchAsync(id, """{"status":"Reviewed"}""", await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Reviewed");
        doc.GetProperty("notes").GetString().Should().Be("original");
    }

    [Test]
    public async Task OnlyNotes_LeavesStatusUnchanged()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, """{"notes":"so notas"}""", await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Received");
        doc.GetProperty("notes").GetString().Should().Be("so notas");
    }

    [TestCase("""{"notes":null}""")]
    [TestCase("""{"notes":""}""")]
    [TestCase("""{"notes":"   "}""")]
    public async Task NullEmptyOrWhitespaceNotes_ClearNotes(string clearBody)
    {
        // Arrange
        var id = await IngestSampleAsync();
        var set = await PatchAsync(id, """{"notes":"temporaria"}""", await GetEtagAsync(id));
        set.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await PatchAsync(id, clearBody, await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("notes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task Notes_AreTrimmed()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, """{"notes":"  com espacos  "}""", await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("notes").GetString().Should().Be("com espacos");
    }

    [Test]
    public async Task Notes_With500Characters_AreAccepted()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var notes = new string('a', 500);

        // Act
        var response = await PatchAsync(id, $$"""{"notes":"{{notes}}"}""", await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AnyStatusTransition_IsAllowed()
    {
        // Arrange
        var id = await IngestSampleAsync();
        (await PatchAsync(id, """{"status":"Archived"}""", await GetEtagAsync(id)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var back = await PatchAsync(id, """{"status":"Received"}""", await GetEtagAsync(id));

        // Assert
        back.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await back.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Received");
    }

    [Test]
    public async Task Patch_DoesNotChangeOtherDocuments()
    {
        // Arrange
        var target = await IngestSampleAsync("nfe-cnpj-numerico.xml");
        var other = await IngestSampleAsync("nfe-cnpj-alfanumerico.xml");
        var etag = await GetEtagAsync(target);

        // Act
        var response = await PatchAsync(target, """{"status":"Archived"}""", etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var untouched = await GetBodyAsync(other);
        untouched.GetProperty("status").GetString().Should().Be("Received");
    }

    // ---------- validação do corpo (400) ----------

    [Test]
    public async Task Notes_With501Characters_Returns400()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var notes = new string('a', 501);

        // Act
        var response = await PatchAsync(id, $$"""{"notes":"{{notes}}"}""", await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [TestCase("""{"status":"1"}""")]
    [TestCase("""{"status":1}""")]
    [TestCase("""{"status":"Foo"}""")]
    [TestCase("""{"status":null}""")]
    [TestCase("""{"status":""}""")]
    public async Task InvalidStatus_Returns400(string body)
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, body, await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestCase("{}")]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[]")]
    public async Task EmptyOrMalformedBody_Returns400(string body)
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, body, await GetEtagAsync(id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestCase("""{"number":"9999"}""")]
    [TestCase("""{"status":"Reviewed","issuerCnpj":"11222333000181"}""")]
    [TestCase("""{"id":"00000000-0000-0000-0000-000000000000"}""")]
    public async Task UnknownField_Returns400_AndChangesNothing(string body)
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);

        // Act
        var response = await PatchAsync(id, body, etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetEtagAsync(id)).Should().Be(etag);
        (await GetBodyAsync(id)).GetProperty("status").GetString().Should().Be("Received");
    }

    // ---------- concorrência (If-Match) ----------

    [Test]
    public async Task WithoutIfMatch_Returns428()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, """{"status":"Reviewed"}""", ifMatch: null);

        // Assert
        response.StatusCode.Should().Be((HttpStatusCode)428);
        (await GetBodyAsync(id)).GetProperty("status").GetString().Should().Be("Received");
    }

    [Test]
    public async Task StaleEtag_Returns412_AndChangesNothing()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var stale = await GetEtagAsync(id);
        (await PatchAsync(id, """{"notes":"primeira"}""", stale))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await PatchAsync(id, """{"notes":"segunda"}""", stale);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await GetBodyAsync(id)).GetProperty("notes").GetString().Should().Be("primeira");
    }

    [Test]
    public async Task WeakEtag_Returns412()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);

        // Act
        var response = await PatchAsync(id, """{"notes":"x"}""", $"W/{etag}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [TestCase("\"nao-e-um-xmin\"")]
    [TestCase("\"99999999999999999999\"")]
    [TestCase("sem-aspas")]
    [TestCase("*")]
    public async Task GarbageEtag_Returns412(string ifMatch)
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, """{"notes":"x"}""", ifMatch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Test]
    public async Task ConcurrentPatches_WithSameEtag_OneSucceedsAndOneGets412()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);

        // Act
        var responses = await Task.WhenAll(
            PatchAsync(id, """{"notes":"A"}""", etag),
            PatchAsync(id, """{"notes":"B"}""", etag));

        // Assert
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed });
    }

    // ---------- 404 / 415 / 401 ----------

    [Test]
    public async Task UnknownId_Returns404_EvenWithoutIfMatch()
    {
        // Act
        var response = await PatchAsync(Guid.NewGuid(), """{"status":"Reviewed"}""", ifMatch: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public async Task UnknownId_Returns404_EvenWithAnyIfMatch()
    {
        // Act
        var response = await PatchAsync(Guid.NewGuid(), """{"status":"Reviewed"}""", "\"123\"");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SoftDeletedDocument_Returns404()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);
        await using (var context = PostgresFixture.CreateContext())
        {
            var document = await context.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == id);
            document.MarkAsDeleted(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        // Act
        var response = await PatchAsync(id, """{"status":"Reviewed"}""", etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestCase("text/plain")]
    [TestCase("application/xml")]
    public async Task NonJsonContentType_Returns415(string mediaType)
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await PatchAsync(id, """{"status":"Reviewed"}""", await GetEtagAsync(id), mediaType);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Test]
    public async Task WithoutApiKey_Returns401()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var etag = await GetEtagAsync(id);
        using var anonymous = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{Route}/{id}")
        {
            Content = new StringContent("""{"status":"Reviewed"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Act
        var response = await anonymous.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
