using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Services.Concrete;

namespace PharmacyDelivery.Infrastructure.Seed;

/// <summary>
/// Marks a seeder as part of a data template chosen by string (<c>Seed:Profile</c>, or <c>[UseSeed("…")]</c> in
/// tests). A class may belong to several templates; <see cref="Order"/> sets the execution order inside a template.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SeedProfileAttribute(string profile) : Attribute
{
    public string Profile { get; } = profile;

    public int Order { get; init; }
}

/// <summary>Builds "template → ordered seeders" from <see cref="SeedProfileAttribute"/> found in the given assemblies.</summary>
public sealed class SeederRegistry
{
    private readonly Dictionary<string, IReadOnlyList<Type>> _profiles;

    /// <summary>Scans public seeder classes of the given assemblies.</summary>
    public SeederRegistry(IEnumerable<Assembly> assemblies)
        : this(assemblies.Distinct().SelectMany(a => a.GetExportedTypes()))
    {
    }

    private SeederRegistry(IEnumerable<Type> candidates)
    {
        var entries = candidates
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISeeder).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<SeedProfileAttribute>().Select(a => (Type: t, Attribute: a)))
            .ToList();

        _profiles = new Dictionary<string, IReadOnlyList<Type>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in entries.GroupBy(e => e.Attribute.Profile, StringComparer.OrdinalIgnoreCase))
        {
            var duplicates = group.GroupBy(e => e.Attribute.Order).Where(g => g.Count() > 1).ToList();
            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Seed profile '{group.Key}' has several seeders with the same Order: "
                    + string.Join("; ", duplicates.Select(d => $"{d.Key}: {string.Join(", ", d.Select(x => x.Type.Name))}")));
            }

            _profiles[group.Key] = group.OrderBy(e => e.Attribute.Order).Select(e => e.Type).ToList();
        }
    }

    /// <summary>Registry built from explicit types (tests).</summary>
    public static SeederRegistry FromTypes(params Type[] seederTypes) => new(seederTypes);

    public IReadOnlyCollection<string> Profiles => _profiles.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<Type> GetSeeders(string profile) =>
        _profiles.TryGetValue(profile, out var seeders)
            ? seeders
            : throw new InvalidOperationException(
                $"Unknown seed profile '{profile}'. Available profiles: {string.Join(", ", Profiles)}.");
}

/// <summary>Name-based GUIDs (RFC 4122 v5 style): the same seed key always yields the same id.</summary>
public static class DeterministicGuid
{
    private static readonly byte[] Namespace = new Guid("6f1f0b8e-6a4f-4f6c-9a43-0b5e8b9a1c11").ToByteArray();

    public static Guid Create(string name)
    {
        var data = Namespace.Concat(Encoding.UTF8.GetBytes(name)).ToArray();
#pragma warning disable CA5350 // SHA-1 is used for identifier derivation (RFC 4122 v5), not for security.
        var hash = SHA1.HashData(data);
#pragma warning restore CA5350
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (little-endian Guid layout)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}

/// <summary>Reads seed JSON files copied next to the binaries (Seed/Data/*.json).</summary>
public sealed class SeedDataSource
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "Seed", "Data");

    public T Read<T>(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, PharmacyDeliveryJsonOptions.Tolerant)
               ?? throw new InvalidOperationException($"Seed file {path} is empty.");
    }
}

/// <summary>
/// Hashes seed passwords exactly like registration does, after checking them against the password policy:
/// a weak password in configuration fails the seed with a clear message.
/// </summary>
public sealed class SeedPasswordHasher(PasswordPolicyValidator policy, IPasswordHasher<User> hasher)
{
    private readonly Dictionary<string, string> _hashes = [];

    public string Hash(string password, string email, string displayName)
    {
        var violations = policy.Validate(password, email, displayName);
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Seed password for {email} violates the password policy: {string.Join("; ", violations.Select(v => v.Code))}.");
        }

        // A hash includes its own salt, so one hash per distinct password is enough (PBKDF2 is deliberately slow).
        var normalized = PasswordPolicyValidator.Normalize(password);
        if (!_hashes.TryGetValue(normalized, out var hash))
        {
            hash = hasher.HashPassword(null!, normalized);
            _hashes[normalized] = hash;
        }

        return hash;
    }
}
