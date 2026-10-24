namespace PharmacyDelivery.Infrastructure.Abstract;

/// <summary>One step of a seed template. Implementations are created per run with dependencies from DI.</summary>
public interface ISeeder
{
    /// <summary>Writes the step's data into the database (and Redis where needed).</summary>
    Task SeedAsync(CancellationToken cancellationToken);
}
