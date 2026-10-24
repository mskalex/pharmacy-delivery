using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Identity;

/// <summary>Account of a customer, pharmacist, courier or admin. Only the password hash is stored.</summary>
public sealed class User : AggregateRoot<UserId>
{
    private User()
    {
    }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Lower-case email used for lookups (unique index).</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Role { get; private set; } = Roles.Customer;

    public string PasswordHash { get; private set; } = string.Empty;

    public PharmacyId? PharmacyId { get; private set; }

    public CourierId? CourierId { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public bool IsBlocked { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset PasswordChangedAt { get; private set; }

    public List<SavedAddress> Addresses { get; private set; } = [];

    public static User Create(
        UserId id,
        string email,
        string displayName,
        string role,
        string passwordHash,
        DateTimeOffset now,
        PharmacyId? pharmacyId = null,
        CourierId? courierId = null)
    {
        if (!Roles.All.Contains(role))
            throw new DomainException(ErrorCodes.ValidationFailed, $"Unknown role '{role}'.");

        if (role == Roles.Pharmacist && pharmacyId is null)
            throw new DomainException(ErrorCodes.ValidationFailed, "A pharmacist must be bound to a pharmacy.");

        if (role == Roles.Courier && courierId is null)
            throw new DomainException(ErrorCodes.ValidationFailed, "A courier account must be bound to a courier.");

        return new User
        {
            Id = id,
            Email = Guard.NotBlank(email).Trim(),
            NormalizedEmail = Normalize(email),
            DisplayName = Guard.NotBlank(displayName).Trim(),
            Role = role,
            PasswordHash = Guard.NotBlank(passwordHash),
            PharmacyId = pharmacyId,
            CourierId = courierId,
            CreatedAt = now,
            PasswordChangedAt = now,
        };
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } end && end > now;

    /// <summary>Registers a failed login; after <paramref name="maxAttempts"/> failures the account is locked.</summary>
    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        if (LockoutEndsAt is { } end && end <= now)
        {
            // Previous lockout expired: start counting from scratch.
            FailedLoginAttempts = 0;
            LockoutEndsAt = null;
        }

        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
            LockoutEndsAt = now.Add(lockoutDuration);
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
    }

    public void ChangePasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = Guard.NotBlank(passwordHash);
        PasswordChangedAt = now;
    }

    /// <summary>Silent hash upgrade after a successful login (hasher parameters were raised).</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = Guard.NotBlank(passwordHash);

    public void Block() => IsBlocked = true;

    public void Unblock() => IsBlocked = false;

    public void AddAddress(SavedAddress address) => Addresses.Add(address);
}

public sealed class SavedAddress
{
    private SavedAddress()
    {
    }

    public SavedAddress(string label, string street, string city, string postalCode, double latitude, double longitude)
    {
        Label = label;
        Street = street;
        City = city;
        PostalCode = postalCode;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string Label { get; private set; } = string.Empty;

    public string Street { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }
}
