using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class RateLimitingTests
{
    private const string Route = "/api/v1/documents";
    private const int Limit = 3;

    private static StringContent EmptyXml() => new("", Encoding.UTF8, "application/xml");

    private static ApiFactory CreateFactory() => new(permitLimit: Limit, windowSeconds: 60);

    private static async Task<HttpResponseMessage> ExceedLimitAsync(HttpClient client)
    {
        for (var i = 0; i < Limit; i++)
            await client.PostAsync(Route, EmptyXml());

        return await client.PostAsync(Route, EmptyXml());
    }

    [Test]
    public async Task RequestsWithinLimit_AreNotRejected()
    {
        // Arrange
        using var factory = CreateFactory();

        using var client = factory.CreateAuthenticatedClient();

        // Act & Assert
        for (var i = 0; i < Limit; i++)
        {
            // Corpo vazio chega ao handler (422) sem tocar no banco.
            var response = await client.PostAsync(Route, EmptyXml());

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
    }

    [Test]
    public async Task RequestBeyondLimit_Returns429()
    {
        // Arrange
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        // Act
        var response = await ExceedLimitAsync(client);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task RejectedRequest_ReturnsProblemDetailsWithRetryAfter()
    {
        // Arrange
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        // Act
        var response = await ExceedLimitAsync(client);

        // Assert
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta!.Value.Should()
            .BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be(429);
    }

    [Test]
    public async Task FailedAuthenticationAttempts_CountTowardTheLimit()
    {
        // Arrange
        using var factory = CreateFactory();
        using var attacker = factory.CreateClient();
        attacker.DefaultRequestHeaders.Add(ApiFactory.ApiKeyHeader, "wrong-key");
        for (var i = 0; i < Limit; i++)
        {
            var failed = await attacker.PostAsync(Route, EmptyXml());
            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        using var legit = factory.CreateAuthenticatedClient();

        // Act
        var response = await legit.PostAsync(Route, EmptyXml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task ExhaustedBucketOfOneIp_DoesNotAffectAnotherIp()
    {
        // Arrange
        using var baseFactory = CreateFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ClientIpStartupFilter>()));
        using var noisy = CreateClientFrom(factory, "10.0.0.1");
        using var quiet = CreateClientFrom(factory, "10.0.0.2");
        (await ExceedLimitAsync(noisy)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Act
        var response = await quiet.PostAsync(Route, EmptyXml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task RequestsFromTheSameIp_ShareOneBucketAcrossClients()
    {
        // Arrange
        using var baseFactory = CreateFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ClientIpStartupFilter>()));
        using var first = CreateClientFrom(factory, "10.0.0.1");
        using var second = CreateClientFrom(factory, "10.0.0.1");
        (await ExceedLimitAsync(first)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Act
        var response = await second.PostAsync(Route, EmptyXml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static HttpClient CreateClientFrom(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string ip)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiFactory.ApiKeyHeader, ApiFactory.TestApiKey);
        client.DefaultRequestHeaders.Add(ClientIpStartupFilter.Header, ip);
        return client;
    }

    // TestServer has no client address, so the test sets one from a header before rate limiting runs.
    private sealed class ClientIpStartupFilter : IStartupFilter
    {
        public const string Header = "X-Test-Client-Ip";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var value)
                    && IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });
            next(app);
        };
    }
}
