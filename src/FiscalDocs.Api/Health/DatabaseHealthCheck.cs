using FiscalDocs.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiscalDocs.Api.Health;

public sealed class DatabaseHealthCheck(FiscalDocsDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext, CancellationToken cancellationToken = default)
    {
        var connect = context.Database.CanConnectAsync(cancellationToken);

        // O Npgsql não interrompe o handshake quando o token é cancelado (o banco pode aceitar a
        // conexão e nunca responder). O WaitAsync devolve o controle assim que o prazo da sonda estoura.
        try
        {
            var canConnect = await connect.WaitAsync(cancellationToken);
            return canConnect ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException)
        {
            _ = connect.ContinueWith(task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }
}
