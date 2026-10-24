using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Domain.Pharmacies;

/// <summary>Partner pharmacy that fulfils orders. Opening hours are evaluated in the pharmacy's local time zone.</summary>
public sealed class Pharmacy : AggregateRoot<PharmacyId>
{
    private Pharmacy()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Street { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public GeoPoint Location { get; private set; } = new(0, 0);

    public OpeningHours OpeningHours { get; private set; } = OpeningHours.NonStop();

    public bool IsActive { get; private set; }

    public static Pharmacy Create(
        PharmacyId id,
        string code,
        string name,
        string street,
        string city,
        string postalCode,
        GeoPoint location,
        OpeningHours openingHours) =>
        new()
        {
            Id = id,
            Code = Guard.NotBlank(code).Trim().ToUpperInvariant(),
            Name = Guard.NotBlank(name).Trim(),
            Street = Guard.NotBlank(street).Trim(),
            City = Guard.NotBlank(city).Trim(),
            PostalCode = Guard.NotBlank(postalCode).Trim(),
            Location = location,
            OpeningHours = openingHours,
            IsActive = true,
        };

    public bool IsOpenAt(DateTimeOffset utcNow, TimeZoneInfo timeZone) =>
        IsActive && OpeningHours.IsOpenAt(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);

    public void Deactivate() => IsActive = false;
}

/// <summary>Weekly schedule. Stored as jsonb; an empty schedule with <see cref="IsNonStop"/> means 24/7.</summary>
public sealed class OpeningHours
{
    private OpeningHours()
    {
    }

    public bool IsNonStop { get; private set; }

    public List<DailyOpeningHours> Days { get; private set; } = [];

    public static OpeningHours NonStop() => new() { IsNonStop = true };

    public static OpeningHours Weekly(IEnumerable<DailyOpeningHours> days) => new() { Days = days.ToList() };

    /// <summary>Open intervals are [Opens, Closes); overnight intervals are not supported by design (24/7 instead).</summary>
    public bool IsOpenAt(DateTime localTime)
    {
        if (IsNonStop)
            return true;

        var time = TimeOnly.FromDateTime(localTime);
        return Days.Any(d => d.Day == localTime.DayOfWeek && time >= d.Opens && time < d.Closes);
    }
}

public sealed class DailyOpeningHours
{
    private DailyOpeningHours()
    {
    }

    public DailyOpeningHours(DayOfWeek day, TimeOnly opens, TimeOnly closes)
    {
        if (closes <= opens)
            throw new ArgumentException("Closing time must be after opening time.", nameof(closes));

        Day = day;
        Opens = opens;
        Closes = closes;
    }

    public DayOfWeek Day { get; private set; }

    public TimeOnly Opens { get; private set; }

    public TimeOnly Closes { get; private set; }
}
