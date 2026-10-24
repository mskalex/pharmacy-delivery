using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Tests;

/// <summary>Unit of work that runs the operation inline (no database) and counts saves.</summary>
public sealed class InlineUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
}

/// <summary>Clock frozen at a given moment; tests move it forward explicitly (<see cref="Advance"/>).</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
