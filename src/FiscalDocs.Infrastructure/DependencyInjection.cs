using FiscalDocs.Application.Abstractions;
using FiscalDocs.Application.Parsing;
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
}
