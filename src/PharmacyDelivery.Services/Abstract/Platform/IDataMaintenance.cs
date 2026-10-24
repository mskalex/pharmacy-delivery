namespace PharmacyDelivery.Services.Abstract;

/// <summary>Reset and reseed of the database (maintenance endpoint, Development/Test only).</summary>
public interface IDataMaintenance
{
    /// <summary>Deletes all application data and loads the seed template (default: Seed:Profile).</summary>
    Task ResetAsync(string? profile, CancellationToken cancellationToken);

    /// <summary>Names of the seed templates that can be loaded.</summary>
    IReadOnlyCollection<string> AvailableProfiles { get; }
}
