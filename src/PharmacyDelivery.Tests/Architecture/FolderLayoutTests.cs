using System.Text.RegularExpressions;

namespace PharmacyDelivery.Tests.Architecture;

/// <summary>
/// Folder rule: interfaces in <c>Abstract</c>, implementations in <c>Concrete</c>, both split into area subfolders
/// (<c>Concrete/Seed</c>, <c>Concrete/Jobs</c>, …). Subfolders only group files: the namespace stays
/// <c>&lt;Project&gt;.Abstract</c> / <c>&lt;Project&gt;.Concrete</c>, so moving a file between groups changes no <c>using</c>.
/// </summary>
[TestFixture]
public sealed partial class FolderLayoutTests : TestBase
{
    [GeneratedRegex(@"^namespace\s+(?<ns>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex NamespaceLine();

    [Test]
    [Property("TestCase", "TC-7.21.9")]
    public void AbstractAndConcrete_AreGroupedIntoAreaFolders_NamespaceStaysAtLayerLevel()
    {
        var problems = new List<string>();
        foreach (var project in RepositoryPaths.ProductProjects())
        {
            foreach (var kind in new[] { "Abstract", "Concrete" })
            {
                var folder = new DirectoryInfo(Path.Combine(project.FullName, kind));
                if (!folder.Exists)
                    continue;

                problems.AddRange(folder.GetFiles("*.cs").Select(f => $"{project.Name}/{kind}/{f.Name}: not in an area subfolder"));
                var expected = $"{project.Name}.{kind}";
                foreach (var file in folder.GetFiles("*.cs", SearchOption.AllDirectories))
                {
                    var ns = NamespaceLine().Match(File.ReadAllText(file.FullName)).Groups["ns"].Value;
                    if (ns != expected)
                        problems.Add($"{Path.GetRelativePath(project.FullName, file.FullName)}: namespace '{ns}', expected '{expected}'");
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }
}
