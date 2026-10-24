namespace PharmacyDelivery.Services.Abstract;

/// <summary>Distributed mutual exclusion (Redis <c>SET NX PX</c> + token-checked release).</summary>
public interface IDistributedLock
{
    /// <summary>Tries to take the lock on <paramref name="resource"/> for at most <paramref name="ttl"/> without waiting.</summary>
    /// <returns>Lock handle to dispose, or null when the resource is held by someone else.</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken);
}
