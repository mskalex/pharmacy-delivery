using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;

namespace PharmacyDelivery.Repository;

/// <summary>
/// Translates PostgreSQL/EF Core failures into application exceptions by SQLSTATE and constraint name
/// (DESIGN.md §13, docs/database.md). Constraint names are set explicitly in EF configurations.
/// </summary>
public sealed class PostgresErrorTranslator(ILogger<PostgresErrorTranslator> logger)
{
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string CheckViolation = "23514";
    public const string LockNotAvailable = "55P03";
    public const string SerializationFailure = "40001";
    public const string DeadlockDetected = "40P01";
    public const string QueryCanceled = "57014";

    /// <summary>Unique constraint → error code returned to the client.</summary>
    public static readonly FrozenDictionary<string, string> UniqueConstraintCodes = new Dictionary<string, string>
    {
        ["ux_payments_provider_payment_id"] = ErrorCodes.PaymentDuplicate,
        ["ux_deliveries_active_courier"] = ErrorCodes.CourierBusy,
        ["ux_deliveries_active_order"] = ErrorCodes.DeliveryDuplicate,
        ["ux_users_email"] = ErrorCodes.EmailTaken,
        ["ux_orders_customer_idempotency_key"] = ErrorCodes.IdempotencyKeyReused,
    }.ToFrozenDictionary();

    public bool TryTranslate(Exception exception, out AppException translated)
    {
        translated = Translate(exception)!;
        return translated is not null;
    }

    /// <returns>Application exception, or null when the exception is not a database failure.</returns>
    public AppException? Translate(Exception exception)
    {
        switch (exception)
        {
            case AppException:
                return null;
            case DbUpdateConcurrencyException concurrency:
                return new ConflictException(ErrorCodes.ConcurrencyConflict, "The resource was modified concurrently. Retry the request.",
                    concurrency);
        }

        var postgres = Find<PostgresException>(exception);
        if (postgres is not null)
            return FromPostgres(postgres, exception);

        if (Find<TimeoutException>(exception) is { } timeout)
            return new DatabaseTimeoutException("Database command timed out.", timeout);

        if (exception is RetryLimitExceededException || Find<NpgsqlException>(exception) is not null)
            return new ServiceUnavailableException("Database is unavailable.", exception);

        return null;
    }

    private AppException FromPostgres(PostgresException postgres, Exception original)
    {
        switch (postgres.SqlState)
        {
            case UniqueViolation:
                var code = postgres.ConstraintName is { } name && UniqueConstraintCodes.TryGetValue(name, out var mapped)
                    ? mapped
                    : ErrorCodes.DatabaseConstraint;
                return new ConflictException(code, "The resource already exists or is already in use.", original);
            case ForeignKeyViolation:
                return new BusinessRuleException(ErrorCodes.DatabaseConstraint, "A referenced resource does not exist.");
            case CheckViolation:
                // The domain should have prevented this: log as an error, it indicates a bug.
                logger.LogError(original, "Check constraint {Constraint} violated", postgres.ConstraintName);
                return new BusinessRuleException(
                    postgres.ConstraintName == "ck_stock_items_quantities"
                        ? ErrorCodes.StockInvariantViolated
                        : ErrorCodes.DatabaseConstraint,
                    "The change violates a data invariant.");
            case LockNotAvailable:
                return new ConflictException(ErrorCodes.StockBusy, "The resource is locked by another operation. Retry the request.",
                    original);
            case SerializationFailure or DeadlockDetected:
                return new ConflictException(ErrorCodes.ConcurrencyConflict, "Concurrent update detected. Retry the request.", original);
            case QueryCanceled:
                return new DatabaseTimeoutException("Database statement timed out.", original);
            default:
                return postgres.IsTransient
                    ? new ServiceUnavailableException("Database is temporarily unavailable.", original)
                    : new ServiceUnavailableException($"Database error {postgres.SqlState}.", original);
        }
    }

    private static T? Find<T>(Exception? exception)
        where T : Exception
    {
        while (exception is not null)
        {
            if (exception is T match)
                return match;

            exception = exception.InnerException;
        }

        return null;
    }
}
