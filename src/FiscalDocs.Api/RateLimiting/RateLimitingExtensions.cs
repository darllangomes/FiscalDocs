using System.Globalization;
using System.Threading.RateLimiting;
using FiscalDocs.Api.Health;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FiscalDocs.Api.RateLimiting;

public static class RateLimitingExtensions
{
    private const string UnknownClient = "unknown";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.PermitLimit > 0 && o.WindowSeconds > 0,
                "RateLimiting:PermitLimit and RateLimiting:WindowSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });

        // Configurado via IOptions (lazy) para enxergar os valores definidos pela factory
        // de testes (UseSetting). Ler builder.Configuration direto aqui não os pegaria.
        services
            .AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
            {
                var cfg = settings.Value;

                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (context.Request.Path.StartsWithSegments(HealthExtensions.BasePath))
                    {
                        return RateLimitPartition.GetNoLimiter("health");
                    }

                    // RemoteIpAddress é null no TestServer, daí o fallback.
                    var client = context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;

                    return RateLimitPartition.GetFixedWindowLimiter(client, _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = cfg.PermitLimit,
                            Window = TimeSpan.FromSeconds(cfg.WindowSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });

                limiter.OnRejected = async (context, cancellationToken) =>
                {
                    var http = context.HttpContext;
                    http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    }

                    var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                    await problems.TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = http,
                        ProblemDetails =
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too many requests.",
                            Detail = "Rate limit exceeded. Retry after the interval indicated in Retry-After."
                        }
                    });
                };
            });

        return services;
    }
}
