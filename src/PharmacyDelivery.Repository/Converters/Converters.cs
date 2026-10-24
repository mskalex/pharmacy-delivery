using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Repository.Converters;

public sealed class ProductIdConverter() : ValueConverter<ProductId, Guid>(id => id.Value, value => new ProductId(value));

public sealed class PharmacyIdConverter() : ValueConverter<PharmacyId, Guid>(id => id.Value, value => new PharmacyId(value));

public sealed class OrderIdConverter() : ValueConverter<OrderId, Guid>(id => id.Value, value => new OrderId(value));

public sealed class CourierIdConverter() : ValueConverter<CourierId, Guid>(id => id.Value, value => new CourierId(value));

public sealed class DeliveryIdConverter() : ValueConverter<DeliveryId, Guid>(id => id.Value, value => new DeliveryId(value));

public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, value => new UserId(value));

/// <summary>Domain <see cref="GeoPoint"/> ↔ PostGIS geography point (SRID 4326, X = longitude, Y = latitude).</summary>
public sealed class GeoPointConverter() : ValueConverter<GeoPoint, Point>(
    point => new Point(point.Longitude, point.Latitude) { SRID = 4326 },
    geometry => new GeoPoint(geometry.Y, geometry.X));
