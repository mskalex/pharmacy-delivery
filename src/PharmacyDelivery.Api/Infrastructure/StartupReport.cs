using System.Collections;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Api.Configuration;

namespace PharmacyDelivery.Api.Infrastructure;

/// <summary>
/// Startup report logged once before the host accepts traffic: host facts, every configuration value of the application sections
/// with the provider that set it (last one wins), and the process environment variables — as a tree. Secrets are masked by key name
/// (password, secret, token, signing key, …) and inside values (connection string passwords, credentials in URLs).
/// </summary>
public static partial class StartupReport
{
    private const string Masked = "***";

    /// <summary>Logs the report (category <c>PharmacyDelivery.Startup</c>) when <see cref="StartupReportOptions.Enabled"/>.</summary>
    public static void Log(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = services.GetRequiredService<IOptions<StartupReportOptions>>().Value;
        if (!options.Enabled || configuration is not IConfigurationRoot root)
            return;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PharmacyDelivery.Startup");
        logger.LogInformation("{StartupReport}", Build(root, environment, options.IncludeEnvironmentVariables));
    }

    /// <summary>Builds the report text.</summary>
    public static string Build(IConfigurationRoot configuration, IHostEnvironment environment, bool includeEnvironmentVariables,
        IDictionary? environmentVariables = null)
    {
        var root = new Node($"PharmacyDelivery API {AppInfo.Version} — startup report");

        var host = root.Add("Host");
        host.Add($"Environment: {environment.EnvironmentName}");
        host.Add($"Application: {environment.ApplicationName} {AppInfo.Version}");
        host.Add($"Content root: {environment.ContentRootPath}");
        host.Add($"Base directory: {AppContext.BaseDirectory}");
        host.Add($"Machine: {Environment.MachineName}, process {Environment.ProcessId}, user {Environment.UserName}");
        host.Add($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        host.Add($"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture}), " +
                 $"server GC: {GCSettings.IsServerGC}, CPUs: {Environment.ProcessorCount}");
        host.Add($"Time zone: {TimeZoneInfo.Local.Id}, culture: {CultureName(CultureInfo.CurrentCulture)}, " +
                 $"UI culture: {CultureName(CultureInfo.CurrentUICulture)}");

        var providers = configuration.Providers.ToList();
        var config = root.Add($"Configuration ({string.Join(" → ", providers.Select(SourceName))}; value ← source)");
        foreach (var section in ApplicationSections(configuration, providers))
            AddSection(config, configuration.GetSection(section), providers);

        if (includeEnvironmentVariables)
        {
            var variables = (environmentVariables ?? Environment.GetEnvironmentVariables()).Cast<DictionaryEntry>()
                .Select(e => (Name: (string)e.Key, Value: e.Value as string ?? string.Empty))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var env = root.Add($"Environment variables ({variables.Count})");
            foreach (var (name, value) in variables)
                env.Add($"{name} = {Display(name, value, isEnvironmentVariable: true)}");
        }

        var text = new StringBuilder();
        root.Render(text, string.Empty, true, true);
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Masks a value if the key names a secret, otherwise masks credentials embedded in the value. Configuration keys are judged by
    /// their last segment (<c>Seed:UsersPassword</c> masked, <c>Auth:TokenRetentionDays</c> not); environment variable names are
    /// judged as a whole (<c>POSTGRES_PASSWORD</c>, <c>GITHUB_TOKEN</c>).
    /// </summary>
    public static string Display(string key, string? value, bool isEnvironmentVariable = false)
    {
        if (string.IsNullOrEmpty(value))
            return "(empty)";

        // Flags are never secrets, even when the key mentions one (Auth:Jwt:AllowEphemeralSigningKey).
        if (bool.TryParse(value, out _))
            return value;

        var secret = isEnvironmentVariable
            ? SecretName().IsMatch(key)
            : SecretKey().IsMatch(key[(key.LastIndexOf(':') + 1)..]);
        if (secret)
            return Masked;

        value = ConnectionStringPassword().Replace(value, "${key}=" + Masked);
        return UrlCredentials().Replace(value, "${scheme}://${user}:" + Masked + "@");
    }

    // Top-level sections that belong to the application: everything defined by a non-environment provider (appsettings*.json,
    // in-memory settings, command line) plus ConnectionStrings. Plain environment variables (PATH, HOME…) go to their own branch.
    private static IEnumerable<string> ApplicationSections(IConfigurationRoot configuration, List<IConfigurationProvider> providers)
    {
        var keys = providers.Where(p => p is not EnvironmentVariablesConfigurationProvider)
            .SelectMany(p => p.GetChildKeys([], null))
            .Append("ConnectionStrings")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => configuration.GetSection(k).Exists())
            .Order(StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    private static void AddSection(Node parent, IConfigurationSection section, List<IConfigurationProvider> providers)
    {
        var children = section.GetChildren().OrderBy(c => c.Key, KeyComparer.Instance).ToList();
        if (children.Count == 0)
        {
            parent.Add($"{section.Key} = {Display(section.Path, section.Value)}  ← {SourceOf(section.Path, providers)}");
            return;
        }

        var node = parent.Add(section.Key);
        foreach (var child in children)
            AddSection(node, child, providers);
    }

    private static string SourceOf(string path, List<IConfigurationProvider> providers)
    {
        for (var i = providers.Count - 1; i >= 0; i--)
        {
            if (providers[i].TryGet(path, out _))
                return SourceName(providers[i]);
        }

        return "?";
    }

    private static string SourceName(IConfigurationProvider provider) =>
        provider switch
        {
            FileConfigurationProvider file => file.Source.Path ?? "file",
            EnvironmentVariablesConfigurationProvider => "env",
            CommandLineConfigurationProvider => "args",
            MemoryConfigurationProvider => "memory",
            _ => provider.GetType().Name.Replace("ConfigurationProvider", string.Empty, StringComparison.Ordinal),
        };

    private static string CultureName(CultureInfo culture) => culture.Name.Length == 0 ? "invariant" : culture.Name;

    [GeneratedRegex(@"(password|passwd|pwd|secret|apikey|api-key|privatekey|credential)|(signingkey|pem|token)$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    [GeneratedRegex(@"(PASSWORD|SECRET|TOKEN|API_?KEY|PRIVATE_?KEY|CREDENTIAL)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    [GeneratedRegex(@"(?<key>password|pwd)\s*=\s*[^;,]*", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();

    [GeneratedRegex(@"(?<scheme>[a-z][a-z0-9+.\-]*)://(?<user>[^:/@\s]+):[^@/\s]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();

    /// <summary>Numeric keys (array items) in numeric order, the rest alphabetically.</summary>
    private sealed class KeyComparer : IComparer<string>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(string? x, string? y) =>
            int.TryParse(x, out var a) && int.TryParse(y, out var b)
                ? a.CompareTo(b)
                : StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }

    private sealed class Node(string text)
    {
        private readonly List<Node> _children = [];

        public Node Add(string childText)
        {
            var child = new Node(childText);
            _children.Add(child);
            return child;
        }

        public void Render(StringBuilder output, string indent, bool isLast, bool isRoot)
        {
            output.Append(indent).Append(isRoot ? string.Empty : isLast ? "└─ " : "├─ ").AppendLine(text);
            var childIndent = isRoot ? string.Empty : indent + (isLast ? "   " : "│  ");
            for (var i = 0; i < _children.Count; i++)
                _children[i].Render(output, childIndent, i == _children.Count - 1, false);
        }
    }
}
