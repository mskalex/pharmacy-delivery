namespace PharmacyDelivery.Api.Configuration;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Enables maintenance endpoints (seed reset). Development and Test only.</summary>
    public bool MaintenanceEndpointsEnabled { get; set; }
}
