using FiscalDocs.Application.Documents;
using FiscalDocs.Application.Ingestion;
using FiscalDocs.Application.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FiscalDocs.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // TryAdd permite que os testes troquem o relógio por um falso.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<DocumentParserResolver>();
        services.AddScoped<IngestDocumentHandler>();
        services.AddScoped<GetDocumentHandler>();
        services.AddScoped<GetDocumentXmlHandler>();
        services.AddScoped<ListDocumentsHandler>();
        services.AddScoped<UpdateDocumentHandler>();
        services.AddScoped<DeleteDocumentHandler>();

        return services;
    }
}
