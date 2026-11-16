using FiscalDocs.Application.Abstractions;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.Infrastructure.Persistence;
using FiscalDocs.Infrastructure.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalDocs.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<FiscalDocsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IFiscalDocumentRepository, FiscalDocumentRepository>();

        services.AddSingleton<IXmlDocumentLoader, SafeXmlDocumentLoader>();
        services.AddSingleton<IDocumentParser, NFeParser>();
        services.AddSingleton<IDocumentParser, CTeParser>();

        return services;
    }

    // Só o Worker fala com o broker: a API grava na outbox e não precisa dele.
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rabbitMqUri = configuration[$"{RabbitMqOptions.SectionName}:Uri"]
            ?? throw new InvalidOperationException("RabbitMq:Uri is not configured.");

        services.AddSingleton(new RabbitMqOptions { Uri = rabbitMqUri });
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IDocumentEventPublisher, RabbitMqDocumentEventPublisher>();

        return services;
    }
}
