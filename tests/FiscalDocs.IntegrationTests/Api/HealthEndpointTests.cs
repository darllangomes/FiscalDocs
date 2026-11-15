using System.Net;
using FluentAssertions;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class HealthEndpointTests
{
    private const string Live = "/health/live";
    private const string Ready = "/health/ready";

    private const string UnreachableBroker = "amqp://guest:guest@127.0.0.1:1/";

    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=secretdb;Username=secretuser;Password=secretpass;Timeout=2;Command Timeout=2";

    private ApiFactory _factory = null!;
    private HttpClient _anonymous = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ApiFactory();
        _anonymous = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    // ---------- live ----------

    [Test]
    public async Task Live_WithoutApiKey_Returns200Healthy()
    {
        // Act
        var response = await _anonymous.GetAsync(Live);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }

    [Test]
    public async Task Live_Returns200_EvenWhenDatabaseIsDown()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: UnreachableDatabase);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(Live);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- ready ----------

    [Test]
    public async Task Ready_WithoutApiKey_Returns200Healthy()
    {
        // Act
        var response = await _anonymous.GetAsync(Ready);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Test]
    public async Task Ready_Returns503Unhealthy_WhenDatabaseIsDown()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: UnreachableDatabase);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(Ready);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
    }

    [Test]
    public async Task Ready_WhenDatabaseIsDown_DoesNotLeakConnectionDetails()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: UnreachableDatabase);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(Ready);

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().NotContainAny("secretdb", "secretuser", "secretpass", "127.0.0.1", "Npgsql");
        factory.Logs.Entries.Should().NotContain(e => e.Contains("secretpass"));
    }

    [Test]
    public async Task Ready_Returns503Unhealthy_WhenTheBrokerIsDown()
    {
        // Arrange
        using var factory = new ApiFactory(rabbitMqUri: UnreachableBroker);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(Ready);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
    }

    [Test]
    public async Task Live_Returns200_EvenWhenTheBrokerIsDown()
    {
        // Arrange
        using var factory = new ApiFactory(rabbitMqUri: UnreachableBroker);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(Live);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- rate limiting ----------

    [TestCase(Live)]
    [TestCase(Ready)]
    public async Task HealthEndpoints_AreNotRateLimited(string path)
    {
        // Arrange
        using var factory = new ApiFactory(permitLimit: 2, windowSeconds: 60);

        using var client = factory.CreateClient();

        // Act & Assert
        for (var i = 0; i < 6; i++)
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"request #{i + 1}");
        }
    }

    [Test]
    public async Task HealthRequests_DoNotConsumeTheApiRateLimit()
    {
        // Arrange
        using var factory = new ApiFactory(permitLimit: 2, windowSeconds: 60);

        using var client = factory.CreateAuthenticatedClient();

        // Act & Assert
        for (var i = 0; i < 5; i++)
        {
            (await client.GetAsync(Live)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await client.GetAsync("/api/v1/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- o resto da API continua protegido ----------

    [Test]
    public async Task ApiRoutes_StillRequireApiKey()
    {
        // Act
        var response = await _anonymous.GetAsync("/api/v1/documents");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UnknownHealthPath_StillRequiresApiKey()
    {
        // Act
        var response = await _anonymous.GetAsync("/health/other");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
