using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace FiscalDocs.Api.Health;

public static class HealthExtensions
{
    public const string BasePath = "/health";
    private const string LivePath = BasePath + "/live";
    private const string ReadyPath = BasePath + "/ready";
    private const string ReadyTag = "ready";

    // Sem timeout próprio o padrão é 30 s: uma sonda de readiness não pode esperar tanto.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: CheckTimeout);

        return services;
    }

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints
            .MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        return endpoints;
    }
}
