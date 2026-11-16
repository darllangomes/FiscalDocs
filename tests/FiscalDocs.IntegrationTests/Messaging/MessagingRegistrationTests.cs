using FiscalDocs.Application.Abstractions;
using FiscalDocs.Infrastructure;
using FiscalDocs.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalDocs.IntegrationTests.Messaging;

[TestFixture]
public class MessagingRegistrationTests
{
    private const string ConnectionString = "Host=localhost;Database=x;Username=x;Password=x";
    private const string BrokerUri = "amqp://guest:guest@localhost:5672/";

    private static IConfiguration Configuration(bool withBroker)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnectionString };
        if (withBroker)
        {
            values["RabbitMq:Uri"] = BrokerUri;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Test]
    public void AddInfrastructure_WithoutBrokerConfiguration_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddInfrastructure(Configuration(withBroker: false));

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public async Task AddInfrastructure_DoesNotRegisterTheBrokerServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure(Configuration(withBroker: true));

        // Act
        await using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<RabbitMqConnectionProvider>().Should().BeNull();
        provider.GetService<IDocumentEventPublisher>().Should().BeNull();
    }

    [Test]
    public void AddMessaging_WithoutTheBrokerUri_ThrowsAtStartup()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddMessaging(Configuration(withBroker: false));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*RabbitMq:Uri*");
    }

    [Test]
    public async Task AddMessaging_WithTheBrokerUri_RegistersOneProviderAndThePublisher()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMessaging(Configuration(withBroker: true));

        // Act
        await using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<RabbitMqConnectionProvider>()
            .Should().BeSameAs(provider.GetRequiredService<RabbitMqConnectionProvider>());
        provider.GetRequiredService<IDocumentEventPublisher>().Should().BeOfType<RabbitMqDocumentEventPublisher>();
        provider.GetRequiredService<RabbitMqConnectionProvider>().Options.Uri.Should().Be(BrokerUri);
    }
}
