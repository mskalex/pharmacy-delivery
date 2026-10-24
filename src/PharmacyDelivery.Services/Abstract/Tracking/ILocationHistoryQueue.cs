using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Bounded queue between GPS ingestion and the batched history writer.</summary>
public interface ILocationHistoryQueue
{
    /// <summary>Queues a GPS point for the history writer without blocking the request.</summary>
    /// <returns>false when the point was dropped because the queue is full.</returns>
    bool TryEnqueue(LocationHistoryPoint point);
}
