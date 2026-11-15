using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class DocumentEventPublishingTests
{
    private const string Route = "/api/v1/documents";
    private const string UnreachableBroker = "amqp://guest:guest@127.0.0.1:1/";
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private RabbitMqTestClient _broker = null!;

    [SetUp]
    public async Task SetUp()
    {
        await using (var context = InfrastructureFixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
        }

        _broker = await RabbitMqTestClient.ConnectAndPurgeAsync();
        _factory = new ApiFactory();
        _client = _factory.CreateAuthenticatedClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        await _broker.DisposeAsync();
    }

    private static StringContent Xml(string file = "nfe-cnpj-numerico.xml") =>
        new(SampleFiles.ReadAllText(file), Encoding.UTF8, "application/xml");

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Test]
    public async Task Post_NewDocument_PublishesOneEventForIt()
    {
        // Act
        var response = await _client.PostAsync(Route, Xml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOfAsync(response);
        var messages = await _broker.ReceiveAsync(1, ReceiveTimeout);
        messages.Should().ContainSingle();
        var json = JsonDocument.Parse(messages[0].Body).RootElement;
        json.GetProperty("documentId").GetGuid().Should().Be(id);
        json.GetProperty("type").GetString().Should().Be("NFe");
        json.GetProperty("issuerCnpj").GetString().Should().Be("11222333000181");
        json.GetProperty("totalAmount").GetDecimal().Should().Be(1500.00m);
    }

    [Test]
    public async Task Post_Event_NeverCarriesRecipientDataOrXml()
    {
        // Act
        await _client.PostAsync(Route, Xml());

        // Assert
        var message = (await _broker.ReceiveAsync(1, ReceiveTimeout)).Single();
        message.Body.Should().NotContain("12345678909");
        message.Body.Should().NotContain("CLIENTE FICTICIO");
        message.Body.Should().NotContain("<");
    }

    [Test]
    public async Task Post_SameDocumentTwice_PublishesOnlyOnce()
    {
        // Arrange
        (await _client.PostAsync(Route, Xml())).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var second = await _client.PostAsync(Route, Xml());

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _broker.ReceiveAllAsync()).Should().ContainSingle();
    }

    [Test]
    public async Task Post_SameKeyWithDifferentContent_PublishesNothingNew()
    {
        // Arrange
        (await _client.PostAsync(Route, Xml())).StatusCode.Should().Be(HttpStatusCode.Created);
        var changed = new StringContent(
            SampleFiles.ReadAllText("nfe-cnpj-numerico.xml") + "\n", Encoding.UTF8, "application/xml");

        // Act
        var response = await _client.PostAsync(Route, changed);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _broker.ReceiveAllAsync()).Should().ContainSingle();
    }

    [Test]
    public async Task Post_RejectedXml_PublishesNothing()
    {
        // Act
        var response = await _client.PostAsync(Route, Xml("invalido.xml"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _broker.ReceiveAllAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Post_WhenTheBrokerIsDown_StillReturns201_PersistsTheDocumentAndLogsTheError()
    {
        // Arrange
        using var factory = new ApiFactory(rabbitMqUri: UnreachableBroker);
        using var client = factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsync(Route, Xml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOfAsync(response);
        (await client.GetAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Logs.Entries.Should().Contain(e => e.Contains("Failed to publish") && e.Contains(id.ToString()));
        factory.Logs.Entries.Should().NotContain(e => e.Contains("12345678909"));
    }
}
