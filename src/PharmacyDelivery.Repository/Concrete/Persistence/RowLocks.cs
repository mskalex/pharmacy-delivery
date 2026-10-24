using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PharmacyDelivery.Repository.Concrete;

/// <summary>
/// Row locks via plain SQL in the current transaction. The aggregate is then loaded with LINQ, which keeps lock
/// statements independent of the entity mapping (owned JSON, complex properties, xmin).
/// </summary>
internal static class RowLocks
{
    /// <returns>true when the row exists and is now locked by this transaction; false when missing or (SKIP LOCKED) busy.</returns>
    public static async Task<bool> TryLockAsync(PharmacyDeliveryDbContext db, string table, Guid id, bool skipLocked,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await db.Database.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
                              ?? throw new InvalidOperationException(
                                  "Row locks require an open transaction (IUnitOfWork.ExecuteInTransactionAsync).");
        // The table name is a constant from code, never user input.
        command.CommandText = $"SELECT id FROM {table} WHERE id = @id FOR UPDATE" + (skipLocked ? " SKIP LOCKED" : string.Empty);
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
