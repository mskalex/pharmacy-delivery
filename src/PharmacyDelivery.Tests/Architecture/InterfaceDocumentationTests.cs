using System.Reflection;
using System.Xml.Linq;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Tests.Architecture;

/// <summary>Every interface and every interface member carries an XML &lt;summary&gt; (project rule).</summary>
[TestFixture]
public sealed class InterfaceDocumentationTests : TestBase
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(IClock).Assembly, typeof(IAggregateRoot).Assembly, typeof(IUnitOfWork).Assembly,
        typeof(IAuthService).Assembly, typeof(ISeeder).Assembly,
    ];

    [Test]
    [Property("TestCase", "TC-7.21.8")]
    public void Interfaces_AndTheirMembers_AreDocumented()
    {
        var missing = new List<string>();
        foreach (var assembly in Assemblies)
        {
            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
            Assert.That(File.Exists(xmlPath), Is.True, $"XML documentation of {assembly.GetName().Name} not found");
            var documented = XDocument.Load(xmlPath).Descendants("member")
                .Where(m => m.Element("summary") is not null)
                .Select(m => m.Attribute("name")!.Value)
                .ToList();

            foreach (var type in assembly.GetTypes()
                         .Where(t => t.IsInterface && t.Namespace?.EndsWith(".Abstract", StringComparison.Ordinal) == true))
            {
                var typeName = type.FullName!.Replace('+', '.');
                if (!documented.Contains("T:" + typeName))
                    missing.Add(typeName);

                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (member is MethodInfo { IsSpecialName: true })
                        continue; // property accessors

                    var prefix = (member.MemberType == MemberTypes.Property ? "P:" : "M:") + typeName + "." + member.Name;

                    bool Matches(string d) =>
                        d == prefix || d.StartsWith(prefix + "(", StringComparison.Ordinal) ||
                        d.StartsWith(prefix + "``", StringComparison.Ordinal);

                    if (!documented.Any(Matches))
                        missing.Add(typeName + "." + member.Name);
                }
            }
        }

        Assert.That(missing, Is.Empty);
    }
}
