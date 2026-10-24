namespace PharmacyDelivery.Repository.Abstract;

/// <summary>
/// Transaction boundary of an application service. Database errors are translated into application exceptions
/// (see PostgresErrorTranslator) before they leave this interface.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Dispatches domain events (MediatR → outbox) and saves all tracked changes; database errors are translated into application exceptions.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> in a database transaction under the retry execution strategy.
    /// On a transient failure the whole operation is re-executed from scratch (the change tracker is cleared),
    /// so it must not have side effects outside the database.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
