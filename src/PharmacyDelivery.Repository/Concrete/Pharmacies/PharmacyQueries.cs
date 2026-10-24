using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class PharmacyQueries(PharmacyDeliveryDbContext db) : IPharmacyQueries
{
    // Pharmacies within the radius (GiST index on location, partial on is_active), ordered by KNN distance,
    // that have every requested line available. Plain ADO.NET: the query is PostGIS-specific SQL and the result is a
    // flat row, so there is nothing for EF to translate or track.
    private const string CandidatesSql = """
                                         WITH point AS (SELECT ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography AS g),
                                              req AS (SELECT * FROM unnest(@product_ids, @quantities) AS r(product_id, quantity))
                                         SELECT p.id,
                                                p.name,
                                                ST_Distance(p.location, point.g) / 1000.0 AS distance_km,
                                                (SELECT count(*) FROM orders o
                                                  WHERE o.pharmacy_id = p.id
                                                    AND o.status IN ('AssignedToPharmacy', 'Accepted', 'ReadyForPickup', 'CourierAssigned'))::int AS active_orders
                                         FROM pharmacies p, point
                                         WHERE p.is_active
                                           AND ST_DWithin(p.location, point.g, @radius_m)
                                           AND (SELECT count(*)
                                                  FROM req
                                                  JOIN stock_items s ON s.product_id = req.product_id AND s.pharmacy_id = p.id
                                                 WHERE s.on_hand - s.reserved >= req.quantity) = @line_count
                                         ORDER BY p.location <-> point.g
                                         LIMIT 20
                                         """;

    private const string CountInRadiusSql = """
                                            SELECT count(*)::int FROM pharmacies p
                                            WHERE p.is_active AND ST_DWithin(p.location, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography, @radius_m)
                                            """;

    public async Task<IReadOnlyList<PharmacyCandidateRow>> FindCandidatesAsync(
        GeoPoint location,
        double radiusKm,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines,
        CancellationToken cancellationToken)
    {
        var merged = lines.GroupBy(l => l.ProductId).Select(g => (ProductId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToList();
        await using var command = await CreateCommandAsync(CandidatesSql, cancellationToken);
        command.Parameters.Add(new NpgsqlParameter("lng", location.Longitude));
        command.Parameters.Add(new NpgsqlParameter("lat", location.Latitude));
        command.Parameters.Add(new NpgsqlParameter("radius_m", radiusKm * 1000));
        command.Parameters.Add(new NpgsqlParameter("product_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            { Value = merged.Select(m => m.ProductId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("quantities", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            { Value = merged.Select(m => m.Quantity).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("line_count", (long)merged.Count));

        var rows = new List<PharmacyCandidateRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new PharmacyCandidateRow(reader.GetGuid(0), reader.GetString(1), reader.GetDouble(2), reader.GetInt32(3)));

        return rows;
    }

    public async Task<int> CountWithinRadiusAsync(GeoPoint location, double radiusKm, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(CountInRadiusSql, cancellationToken);
        command.Parameters.Add(new NpgsqlParameter("lng", location.Longitude));
        command.Parameters.Add(new NpgsqlParameter("lat", location.Latitude));
        command.Parameters.Add(new NpgsqlParameter("radius_m", radiusKm * 1000));
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public Task<PharmacyInfo?> GetInfoAsync(Guid pharmacyId, CancellationToken cancellationToken)
    {
        var id = new PharmacyId(pharmacyId);
        return db.Pharmacies.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PharmacyInfo(p.Id.Value, p.Code, p.Name, p.Street, p.City, p.PostalCode, p.Location.Latitude,
                p.Location.Longitude))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PharmacyInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var pharmacies = await db.Pharmacies.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return pharmacies.Select(QueryMapping.ToInfo).ToList();
    }

    private async Task<DbCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await db.Database.OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }
}
