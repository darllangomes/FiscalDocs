using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class DocumentOutboxTests
{
    private const string Route = "/api/v1/documents";
    private const string UnreachableBroker = "amqp://guest:guest@127.0.0.1:1/";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private RabbitMqTestClient _broker = null!;

    [SetUp]
    public async Task SetUp()
    {
        await using (var context = InfrastructureFixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents, outbox_messages");
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

    private static async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    [Test]
    public async Task Post_NewDocument_StoresOneOutboxMessageForIt()
    {
        // Act
        var response = await _client.PostAsync(Route, Xml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOfAsync(response);
        var message = (await OutboxAsync()).Should().ContainSingle().Subject;
        message.ProcessedAt.Should().BeNull();
        var json = JsonDocument.Parse(message.Payload).RootElement;
        json.GetProperty("messageId").GetGuid().Should().Be(message.Id);
        json.GetProperty("documentId").GetGuid().Should().Be(id);
        json.GetProperty("type").GetString().Should().Be("NFe");
        json.GetProperty("issuerCnpj").GetString().Should().Be("11222333000181");
        json.GetProperty("totalAmount").GetDecimal().Should().Be(1500.00m);
    }

    [Test]
    public async Task Post_NewDocument_DoesNotTouchTheBroker()
    {
        // Act
        var response = await _client.PostAsync(Route, Xml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _broker.ReceiveAllAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Post_Payload_NeverCarriesRecipientDataOrXml()
    {
        // Act
        await _client.PostAsync(Route, Xml());

        // Assert
        var payload = (await OutboxAsync()).Single().Payload;
        payload.Should().NotContain("12345678909");
        payload.Should().NotContain("CLIENTE FICTICIO");
        payload.Should().NotContain("<");
    }

    [Test]
    public async Task Post_SameDocumentTwice_StoresOnlyOneMessage()
    {
        // Arrange
        (await _client.PostAsync(Route, Xml())).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var second = await _client.PostAsync(Route, Xml());

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await OutboxAsync()).Should().ContainSingle();
    }

    [Test]
    public async Task Post_SameKeyWithDifferentContent_StoresNothingNew()
    {
        // Arrange
        (await _client.PostAsync(Route, Xml())).StatusCode.Should().Be(HttpStatusCode.Created);
        var changed = new StringContent(
            SampleFiles.ReadAllText("nfe-cnpj-numerico.xml") + "\n", Encoding.UTF8, "application/xml");

        // Act
        var response = await _client.PostAsync(Route, changed);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OutboxAsync()).Should().ContainSingle();
    }

    [Test]
    public async Task Post_RejectedXml_StoresNothing()
    {
        // Act
        var response = await _client.PostAsync(Route, Xml("invalido.xml"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await OutboxAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Post_WhenTheBrokerIsDown_Returns201AndKeepsTheEventForLater()
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
        (await OutboxAsync()).Should().ContainSingle().Which.ProcessedAt.Should().BeNull();
        factory.Logs.Entries.Should().NotContain(e => e.Contains("Failed to publish"));
        factory.Logs.Entries.Should().NotContain(e => e.Contains("12345678909"));
    }
}
