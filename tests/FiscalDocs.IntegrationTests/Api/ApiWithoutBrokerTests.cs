using System.Net;
using System.Text;
using FiscalDocs.Application.Abstractions;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class ApiWithoutBrokerTests
{
    private const string Route = "/api/v1/documents";

    [SetUp]
    public async Task SetUp()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents, outbox_messages");
    }

    [Test]
    public void Api_DoesNotRegisterAnyBrokerService()
    {
        // Arrange
        using var factory = new ApiFactory();

        // Act
        _ = factory.Server;

        // Assert
        factory.Services.GetService<RabbitMqConnectionProvider>().Should().BeNull();
        factory.Services.GetService<IDocumentEventPublisher>().Should().BeNull();
    }

    [Test]
    public async Task Ready_DependsOnlyOnTheDatabase()
    {
        // Arrange
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Select(r => r.Name).Should().Equal("database");
    }

    [Test]
    public async Task Post_WithoutAnyBrokerConfiguration_Returns201AndKeepsTheEventForLater()
    {
        // Arrange
        using var factory = new ApiFactory();
        using var client = factory.CreateAuthenticatedClient();
        var xml = new StringContent(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), Encoding.UTF8, "application/xml");

        // Act
        var response = await client.PostAsync(Route, xml);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var context = InfrastructureFixture.CreateContext();
        (await context.OutboxMessages.AsNoTracking().ToListAsync()).Should().ContainSingle()
            .Which.ProcessedAt.Should().BeNull();
        factory.Logs.Entries.Should().NotContain(e => e.Contains("Failed to publish"));
    }
}
