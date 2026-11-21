using DevOpsLab.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DevOpsLab.WebApi.HealthChecks;

/// <summary>
/// Readiness check. The service is only "ready" when the database is reachable
/// AND every migration has been applied.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDatabaseConnectivityProbe _probe;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(IDatabaseConnectivityProbe probe, ILogger<DatabaseHealthCheck> logger)
    {
        _probe = probe;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Race the connectivity check against a strict 1.5-second timeout task
            var connectTask = _probe.CanConnectAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                return HealthCheckResult.Unhealthy("Database connection timed out.");
            }

            if (!await connectTask)
            {
                return HealthCheckResult.Unhealthy("Database is not reachable.");
            }

            // Race the migration check against a strict 1.5-second timeout task
            var migrationTask = _probe.HasPendingMigrationsAsync(cancellationToken);
            var migrationTimeoutTask = Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);

            var completedMigrationTask = await Task.WhenAny(migrationTask, migrationTimeoutTask);
            if (completedMigrationTask == migrationTimeoutTask)
            {
                return HealthCheckResult.Unhealthy("Migration check timed out.");
            }

            if (await migrationTask)
            {
                return HealthCheckResult.Unhealthy("Database has pending migrations.");
            }

            return HealthCheckResult.Healthy("Database is reachable and up to date.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness probe failed while checking the database.");
            return HealthCheckResult.Unhealthy("Readiness probe threw an exception.", ex);
        }
    }
}