namespace PharmacyDelivery.Tests;

/// <summary>Locates files of the repository for tests that check sources and documentation.</summary>
public static class RepositoryPaths
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (DESIGN.md) not found.");
    });

    /// <summary>Repository root (the folder with DESIGN.md).</summary>
    public static string Root => RootPath.Value;

    /// <summary>Product projects in <c>src</c> (without the test project).</summary>
    public static IEnumerable<DirectoryInfo> ProductProjects() =>
        new DirectoryInfo(Path.Combine(Root, "src")).GetDirectories("PharmacyDelivery.*")
            .Where(d => !d.Name.EndsWith("Tests", StringComparison.Ordinal) && d.GetFiles("*.csproj").Length > 0);
}
