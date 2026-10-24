using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PharmacyDelivery.Api.Configuration;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Repository.Options;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Tests.Architecture;

/// <summary>Keeps documentation and code in sync: configuration reference and test plan.</summary>
[TestFixture]
public sealed partial class DocumentationTests : TestBase
{
    private static string Root => RepositoryPaths.Root;

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    private static IEnumerable<string> Leaves(JsonElement element, string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var leaf in Leaves(property.Value, prefix.Length == 0 ? property.Name : prefix + ":" + property.Name))
                    yield return leaf;
            }
        }
        else
        {
            yield return prefix; // scalars and arrays are documented as one key
        }
    }

    private static List<string> SettingKeys(params string[] path)
    {
        using var json = JsonDocument.Parse(Read(path));
        return Leaves(json.RootElement, string.Empty)
            .Where(k => !k.StartsWith("NLog", StringComparison.Ordinal) && !k.StartsWith("Logging", StringComparison.Ordinal))
            .ToList();
    }

    [Test]
    [Property("TestCase", "TC-7.18.1")]
    public void EverySetting_IsDocumented_AndEveryOptionsPropertyHasASetting()
    {
        var documentation = Read("docs", "configuration.md");
        var apiKeys = SettingKeys("src", "PharmacyDelivery.Api", "appsettings.json");
        var simulatorKeys = SettingKeys("src", "PharmacyDelivery.CourierSimulator", "appsettings.json");

        var undocumented = apiKeys.Concat(simulatorKeys).Where(k => !documentation.Contains('`' + k + '`', StringComparison.Ordinal))
            .ToList();
        Assert.That(undocumented, Is.Empty, "keys missing in docs/configuration.md");

        var optionTypes = new[]
        {
            typeof(CheckoutOptions), typeof(PharmacyOptions), typeof(DispatchOptions), typeof(TrackingOptions), typeof(EtaOptions),
            typeof(PaymentsOptions), typeof(AuthOptions), typeof(DatabaseOptions), typeof(AuditOptions), typeof(RedisOptions),
            typeof(RabbitMqOptions), typeof(SeedOptions), typeof(JobsOptions), typeof(OpenTelemetryOptions), typeof(SwaggerOptions),
            typeof(CorsOptions), typeof(SecurityOptions), typeof(RateLimitingOptions), typeof(ErrorHandlingOptions), typeof(AdminOptions),
        };
        var expected = optionTypes.SelectMany(t => OptionKeys(t, (string)t.GetField("SectionName")!.GetValue(null)!)).ToList();
        Assert.That(expected.Except(apiKeys), Is.Empty, "options properties missing in appsettings.json");
    }

    private static IEnumerable<string> OptionKeys(Type type, string prefix)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var key = prefix + ":" + property.Name;
            var nested = property.PropertyType;
            if (nested.IsClass && nested != typeof(string) && !nested.IsArray &&
                nested.Namespace?.StartsWith("PharmacyDelivery", StringComparison.Ordinal) == true)
            {
                foreach (var child in OptionKeys(nested, key))
                    yield return child;
            }
            else
            {
                yield return key;
            }
        }
    }

    [GeneratedRegex(@"^#### (?<num>7\.(?<m>\d+)\.(?<k>\d+)) ", RegexOptions.Multiline)]
    private static partial Regex CaseHeading();

    [GeneratedRegex(@"^### 7\.(?<m>\d+) ", RegexOptions.Multiline)]
    private static partial Regex AreaHeading();

    [GeneratedRegex(@"^`(?<id>TC-7\.\d+\.\d+)` · .*automation: (?<auto>yes|no|retired)", RegexOptions.Multiline)]
    private static partial Regex CaseMeta();

    [GeneratedRegex(@"^- (?<step>7\.\d+\.\d+\.(?<s>\d+)) ", RegexOptions.Multiline)]
    private static partial Regex Step();

    [GeneratedRegex(@"Property\(""TestCase"", ""(?<id>TC-[0-9.]+)""\)")]
    private static partial Regex TestCaseAttribute();

    [Test]
    [Property("TestCase", "TC-7.21.7")]
    public void TestPlan_NumberingIsContinuous_AndMatchesAutomatedTests()
    {
        var plan = Read("docs", "test-plan.md");

        // Areas 7.1, 7.2, … without gaps; cases 7.M.1, 7.M.2, … without gaps; steps 7.M.K.1, … without gaps.
        var areas = AreaHeading().Matches(plan).Select(m => int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture)).ToList();
        Assert.That(areas, Is.EqualTo(Enumerable.Range(1, areas.Count)), "area numbering");

        var cases = CaseHeading().Matches(plan).ToList();
        foreach (var area in cases.GroupBy(c => c.Groups["m"].Value))
        {
            var numbers = area.Select(c => int.Parse(c.Groups["k"].Value, CultureInfo.InvariantCulture)).ToList();
            Assert.That(numbers, Is.EqualTo(Enumerable.Range(1, numbers.Count)), $"case numbering in 7.{area.Key}");
        }

        var sections = plan.Split("\n#### ").Skip(1).ToList();
        foreach (var section in sections)
        {
            var caseNumber = section[..section.IndexOf(' ', StringComparison.Ordinal)];
            var steps = Step().Matches(section).Select(m => m.Groups["step"].Value).ToList();
            Assert.That(steps, Is.Not.Empty, $"case {caseNumber} has no steps");
            Assert.That(steps, Is.EqualTo(Enumerable.Range(1, steps.Count).Select(s => $"{caseNumber}.{s}")),
                $"step numbering in {caseNumber}");
        }

        var meta = CaseMeta().Matches(plan).ToDictionary(m => m.Groups["id"].Value, m => m.Groups["auto"].Value);
        Assert.That(meta.Keys, Is.EquivalentTo(cases.Select(c => "TC-" + c.Groups["num"].Value)), "every case has its metadata line");

        var testIds = Directory.EnumerateFiles(Path.Combine(Root, "src", "PharmacyDelivery.Tests"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => TestCaseAttribute().Matches(File.ReadAllText(f)).Select(m => m.Groups["id"].Value))
            .ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(meta.Where(m => m.Value == "yes").Select(m => m.Key).Where(id => !testIds.Contains(id)), Is.Empty,
                "automated cases without a test");
            Assert.That(testIds.Where(id => !meta.TryGetValue(id, out var auto) || auto != "yes"), Is.Empty,
                "tests pointing to unknown, manual or withdrawn cases");
        });
    }
}
