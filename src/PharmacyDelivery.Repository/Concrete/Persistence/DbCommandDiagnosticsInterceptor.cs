using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PharmacyDelivery.Repository.Options;

namespace PharmacyDelivery.Repository.Concrete;

/// <summary>
/// Logs failed commands (SQLSTATE, constraint, duration, SQL text without parameter values) and slow commands.
/// Parameter values are logged only when EnableSensitiveDataLogging is on (Development).
/// </summary>
public sealed class DbCommandDiagnosticsInterceptor(
    IOptions<DatabaseOptions> options,
    ILogger<DbCommandDiagnosticsInterceptor> logger) : DbCommandInterceptor
{
    private readonly TimeSpan _slowThreshold = TimeSpan.FromMilliseconds(options.Value.SlowQueryThresholdMs);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        ReportIfSlow(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ReportIfSlow(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        ReportIfSlow(command, eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Exception is PostgresException { SqlState: "42P01" } &&
            command.CommandText.Contains("__ef_migrations_history", StringComparison.Ordinal))
        {
            // First start on an empty database: EF probes the migrations table before creating it.
        }
        else if (eventData.Exception is PostgresException postgres)
        {
            // Lock timeouts and unique violations are expected under contention: they are handled and reported as 409.
            var expected = postgres.SqlState is PostgresErrorTranslator.LockNotAvailable or PostgresErrorTranslator.UniqueViolation;
            logger.Log(
                expected ? LogLevel.Warning : LogLevel.Error,
                expected ? null : eventData.Exception,
                "Database command failed: {SqlState} {Constraint} after {DurationMs} ms. Sql: {Sql}",
                postgres.SqlState,
                postgres.ConstraintName,
                eventData.Duration.TotalMilliseconds,
                command.CommandText);
        }
        else if (eventData.Exception is not OperationCanceledException)
        {
            logger.LogError(
                eventData.Exception,
                "Database command failed after {DurationMs} ms. Sql: {Sql}",
                eventData.Duration.TotalMilliseconds,
                command.CommandText);
        }

        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private void ReportIfSlow(DbCommand command, TimeSpan duration)
    {
        if (duration >= _slowThreshold)
            logger.LogWarning("Slow database command: {DurationMs} ms. Sql: {Sql}", duration.TotalMilliseconds, command.CommandText);
    }
}
