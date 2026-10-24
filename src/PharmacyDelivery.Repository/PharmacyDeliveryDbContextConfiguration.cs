using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyDelivery.Repository.Options;

namespace PharmacyDelivery.Repository;

/// <summary>Single place where the Npgsql provider is configured (runtime and design time).</summary>
public static class PharmacyDeliveryDbContextConfiguration
{
    public const string ConnectionStringName = "Postgres";

    public static void Configure(DbContextOptionsBuilder builder, string connectionString, DatabaseOptions options)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // Server-side statement timeout for every session (DESIGN.md §13).
            Options = $"-c statement_timeout={options.StatementTimeoutMs}",
        };

        builder
            .UseNpgsql(connection.ConnectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(options.MaxRetryCount, TimeSpan.FromSeconds(options.MaxRetryDelaySeconds),
                    errorCodesToAdd: null);
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
            })
            .UseSnakeCaseNamingConvention();

        if (options.EnableSensitiveDataLogging)
            builder.EnableSensitiveDataLogging();
    }
}
