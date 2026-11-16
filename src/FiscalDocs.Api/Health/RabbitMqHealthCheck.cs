using FiscalDocs.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiscalDocs.Api.Health;

public sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connections.GetConnectionAsync(cancellationToken);
            return connection.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
