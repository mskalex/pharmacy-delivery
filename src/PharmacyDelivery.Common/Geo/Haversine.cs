namespace PharmacyDelivery.Common.Geo;

/// <summary>Great-circle distance on a spherical Earth. Accurate to ~0.5% which is plenty for city-scale ETA.</summary>
public static class Haversine
{
    public const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Point located <paramref name="fraction"/> of the way from the first to the second point (linear in lat/lng).</summary>
    public static (double Lat, double Lng) Interpolate(double lat1, double lng1, double lat2, double lng2, double fraction)
    {
        var f = Math.Clamp(fraction, 0, 1);
        return (lat1 + ((lat2 - lat1) * f), lng1 + ((lng2 - lng1) * f));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
