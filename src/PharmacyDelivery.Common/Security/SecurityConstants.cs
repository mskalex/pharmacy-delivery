namespace PharmacyDelivery.Common.Security;

/// <summary>Application roles. Values are stored in the database and issued in the <c>role</c> claim.</summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string Pharmacist = "Pharmacist";
    public const string Courier = "Courier";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = [Customer, Pharmacist, Courier, Admin];
}

/// <summary>JWT claim names used by the API (no inbound claim mapping is applied).</summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string Email = "email";
    public const string Name = "name";
    public const string PharmacyId = "pharmacy_id";
    public const string CourierId = "courier_id";
    public const string SessionId = "sid";
    public const string TokenId = "jti";
}
