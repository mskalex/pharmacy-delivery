namespace PharmacyDelivery.Api.Configuration;

public sealed class StartupReportOptions
{
    public const string SectionName = "StartupReport";

    /// <summary>Logs the startup report: host, every configuration value with its source, environment variables (secrets masked).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Adds the process environment variables to the report.</summary>
    public bool IncludeEnvironmentVariables { get; set; } = true;
}
