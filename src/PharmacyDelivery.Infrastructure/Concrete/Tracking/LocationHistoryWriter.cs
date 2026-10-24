using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Consumes the channel and writes history in batches with PostgreSQL binary COPY (orders of magnitude faster
/// than INSERT per point). A batch is flushed when full or after the flush interval.
/// </summary>
public sealed class LocationHistoryWriter(
    LocationHistoryChannel channel,
    IServiceScopeFactory scopes,
    IOptions<TrackingOptions> options,
    ILogger<LocationHistoryWriter> logger) : BackgroundService
{
    private const string CopyCommand =
        "COPY courier_locations (courier_id, delivery_id, latitude, longitude, speed_kmh, recorded_at) FROM STDIN (FORMAT BINARY)";

    private readonly TrackingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<LocationHistoryPoint>(_options.HistoryBatchSize);
        try
        {
            while (await channel.Reader.WaitToReadAsync(stoppingToken))
            {
                await FillBatchAsync(batch, stoppingToken);
                await WriteAsync(batch, stoppingToken);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: flush what is already buffered.
            while (channel.Reader.TryRead(out var point))
                batch.Add(point);

            await WriteAsync(batch, CancellationToken.None);
        }
    }

    private async Task FillBatchAsync(List<LocationHistoryPoint> batch, CancellationToken stoppingToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(_options.HistoryFlushIntervalMs);
        while (batch.Count < _options.HistoryBatchSize)
        {
            while (batch.Count < _options.HistoryBatchSize && channel.Reader.TryRead(out var point))
                batch.Add(point);

            var remaining = deadline - DateTime.UtcNow;
            if (batch.Count >= _options.HistoryBatchSize || remaining <= TimeSpan.Zero)
                return;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(remaining);
            try
            {
                if (!await channel.Reader.WaitToReadAsync(timeout.Token))
                    return;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                return; // flush interval elapsed
            }
        }
    }

    private async Task WriteAsync(List<LocationHistoryPoint> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyDeliveryDbContext>();
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            await using var importer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);
            foreach (var point in batch)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(point.CourierId, NpgsqlDbType.Uuid, cancellationToken);
                if (point.DeliveryId is { } deliveryId)
                    await importer.WriteAsync(deliveryId, NpgsqlDbType.Uuid, cancellationToken);
                else
                    await importer.WriteNullAsync(cancellationToken);

                await importer.WriteAsync(point.Latitude, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(point.Longitude, NpgsqlDbType.Double, cancellationToken);
                if (point.SpeedKmh is { } speed)
                    await importer.WriteAsync(speed, NpgsqlDbType.Double, cancellationToken);
                else
                    await importer.WriteNullAsync(cancellationToken);

                await importer.WriteAsync(point.RecordedAt, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            PharmacyDeliveryTelemetry.TrackingPointsDropped.Add(batch.Count, new KeyValuePair<string, object?>("reason", "write_failed"));
            logger.LogError(ex, "Writing {Count} location points failed; batch dropped", batch.Count);
        }
    }
}
