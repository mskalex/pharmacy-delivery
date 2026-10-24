using System.Collections.Frozen;
using System.Text;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Password policy aligned with NIST SP 800-63B: length and a denylist matter more than composition rules
/// (which are configurable and off by default). History is checked separately by <see cref="AuthService"/>
/// because it needs the stored hashes. See DESIGN.md §15 and docs/configuration.md (<c>Auth:Password</c>).
/// </summary>
public sealed class PasswordPolicyValidator
{
    private const string ResourceName = "PharmacyDelivery.Services.Auth.common-passwords.txt";
    private static readonly Lazy<FrozenSet<string>> CommonPasswords = new(LoadCommonPasswords);

    private readonly PasswordPolicyOptions _options;

    public PasswordPolicyValidator(IOptions<AuthOptions> options)
    {
        _options = options.Value.Password;
    }

    /// <summary>Unicode normalization applied before validation and hashing, so visually equal passwords match.</summary>
    public static string Normalize(string password) => password.Normalize(NormalizationForm.FormKC);

    public IReadOnlyList<PasswordViolation> Validate(string password, string? email, string? displayName)
    {
        var violations = new List<PasswordViolation>();
        var normalized = Normalize(password ?? string.Empty);
        var length = normalized.EnumerateRunes().Count();

        if (length < _options.MinLength)
            violations.Add(new(ErrorCodes.PasswordTooShort, $"Password must be at least {_options.MinLength} characters long."));

        if (length > _options.MaxLength)
            violations.Add(new(ErrorCodes.PasswordTooLong, $"Password must be at most {_options.MaxLength} characters long."));

        if (CommonPasswords.Value.Contains(normalized.ToLowerInvariant()))
            violations.Add(new(ErrorCodes.PasswordCommon, "Password is too common."));

        if (ContainsPersonalData(normalized, email, displayName))
            violations.Add(new(ErrorCodes.PasswordContainsPersonalData, "Password must not contain your email, name or the service name."));

        var compositionErrors = new List<string>();
        if (_options.RequireUppercase && !normalized.Any(char.IsUpper))
            compositionErrors.Add("an uppercase letter");

        if (_options.RequireLowercase && !normalized.Any(char.IsLower))
            compositionErrors.Add("a lowercase letter");

        if (_options.RequireDigit && !normalized.Any(char.IsDigit))
            compositionErrors.Add("a digit");

        if (_options.RequireNonAlphanumeric && normalized.All(char.IsLetterOrDigit))
            compositionErrors.Add("a non-alphanumeric character");

        if (compositionErrors.Count > 0)
            violations.Add(new(ErrorCodes.PasswordComposition, "Password must contain " + string.Join(", ", compositionErrors) + "."));

        return violations;
    }

    private static bool ContainsPersonalData(string password, string? email, string? displayName)
    {
        var lower = password.ToLowerInvariant();
        var fragments = new List<string> { "pharmacy" };
        if (!string.IsNullOrWhiteSpace(email))
        {
            fragments.Add(email.Trim().ToLowerInvariant());
            fragments.Add(email.Split('@')[0].Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(displayName))
            fragments.AddRange(displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.ToLowerInvariant()));

        return fragments.Where(f => f.Length >= 3).Any(lower.Contains);
    }

    private static FrozenSet<string> LoadCommonPasswords()
    {
        using var stream = typeof(PasswordPolicyValidator).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
                set.Add(line.Trim().ToLowerInvariant());
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }
}
