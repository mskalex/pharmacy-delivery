using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace PharmacyDelivery.Tests.Infrastructure;

[TestFixture]
public sealed class PostgresErrorTranslatorTests : TestBase
{
    private readonly PostgresErrorTranslator _translator = new(NullLogger<PostgresErrorTranslator>.Instance);

    private static PostgresException Postgres(string sqlState, string? constraint = null) =>
        new("message", "ERROR", "ERROR", sqlState, constraintName: constraint);

    [TestCase(PostgresErrorTranslator.UniqueViolation, "ux_payments_provider_payment_id", typeof(ConflictException),
        ErrorCodes.PaymentDuplicate)]
    [TestCase(PostgresErrorTranslator.UniqueViolation, "ux_deliveries_active_courier", typeof(ConflictException), ErrorCodes.CourierBusy)]
    [TestCase(PostgresErrorTranslator.UniqueViolation, "ux_users_email", typeof(ConflictException), ErrorCodes.EmailTaken)]
    [TestCase(PostgresErrorTranslator.UniqueViolation, "ux_unknown", typeof(ConflictException), ErrorCodes.DatabaseConstraint)]
    [TestCase(PostgresErrorTranslator.ForeignKeyViolation, null, typeof(BusinessRuleException), ErrorCodes.DatabaseConstraint)]
    [TestCase(PostgresErrorTranslator.CheckViolation, "ck_stock_items_quantities", typeof(BusinessRuleException),
        ErrorCodes.StockInvariantViolated)]
    [TestCase(PostgresErrorTranslator.LockNotAvailable, null, typeof(ConflictException), ErrorCodes.StockBusy)]
    [TestCase(PostgresErrorTranslator.DeadlockDetected, null, typeof(ConflictException), ErrorCodes.ConcurrencyConflict)]
    [TestCase(PostgresErrorTranslator.SerializationFailure, null, typeof(ConflictException), ErrorCodes.ConcurrencyConflict)]
    [TestCase(PostgresErrorTranslator.QueryCanceled, null, typeof(DatabaseTimeoutException), ErrorCodes.DatabaseTimeout)]
    [Property("TestCase", "TC-7.14.1")]
    public void Translate_MapsSqlStateAndConstraint(string sqlState, string? constraint, Type expectedType, string expectedCode)
    {
        var translated = _translator.Translate(new DbUpdateException("save failed", Postgres(sqlState, constraint)));

        Assert.That(translated, Is.InstanceOf(expectedType));
        Assert.That(translated!.Code, Is.EqualTo(expectedCode));
    }

    [Test]
    [Property("TestCase", "TC-7.14.2")]
    public void Translate_ConcurrencyAndConnectivityFailures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_translator.Translate(new DbUpdateConcurrencyException("x"))!.Code, Is.EqualTo(ErrorCodes.ConcurrencyConflict));
            Assert.That(_translator.Translate(new NpgsqlException("connection refused")), Is.InstanceOf<ServiceUnavailableException>());
            Assert.That(_translator.Translate(new InvalidOperationException("not a database error")), Is.Null);
        });
    }
}
