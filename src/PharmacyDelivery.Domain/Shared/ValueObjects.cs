using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Geo;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Shared;

/// <summary>Money in a single currency. Amounts are decimals rounded to cents (never floating point).</summary>
public sealed record Money
{
    public const string Usd = "USD";

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new DomainException(ErrorCodes.ValidationFailed, "Money amount cannot be negative.");

        if (currency != Usd)
            throw new DomainException(ErrorCodes.ValidationFailed, $"Unsupported currency '{currency}'.");

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero => new(0, Usd);

    public static Money OfUsd(decimal amount) => new(amount, Usd);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity) => new(Amount * quantity, Currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
            throw new DomainException(ErrorCodes.ValidationFailed, "Currency mismatch.");
    }
}

/// <summary>WGS84 coordinates.</summary>
public sealed record GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || double.IsNaN(latitude))
            throw new DomainException(ErrorCodes.ValidationFailed, "Latitude must be within [-90, 90].");

        if (longitude is < -180 or > 180 || double.IsNaN(longitude))
            throw new DomainException(ErrorCodes.ValidationFailed, "Longitude must be within [-180, 180].");

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public double DistanceKmTo(GeoPoint other) =>
        Haversine.DistanceKm(Latitude, Longitude, other.Latitude, other.Longitude);
}

/// <summary>Delivery address snapshot stored with an order (later edits of saved addresses do not affect it).</summary>
public sealed class DeliveryAddress
{
    private DeliveryAddress()
    {
    }

    public DeliveryAddress(string street, string city, string postalCode, double latitude, double longitude)
    {
        Street = street.Trim();
        City = city.Trim();
        PostalCode = postalCode.Trim();
        var point = new GeoPoint(latitude, longitude);
        Latitude = point.Latitude;
        Longitude = point.Longitude;
    }

    public string Street { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public GeoPoint Location => new(Latitude, Longitude);
}
