namespace PharmacyDelivery.Api.Controllers;

/// <summary>Authorization policy names.</summary>
public static class Policies
{
    public const string Customer = "customer";
    public const string Pharmacist = "pharmacist";
    public const string Courier = "courier";
    public const string Admin = "admin";
}
