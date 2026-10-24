using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Infrastructure.Seed;

internal sealed record PharmacyJson(
    string Code,
    string Name,
    string Street,
    string City,
    string PostalCode,
    double Latitude,
    double Longitude,
    bool NonStop,
    HoursJson? Weekdays,
    HoursJson? Saturday,
    HoursJson? Sunday)
{
    public OpeningHours ToOpeningHours()
    {
        if (NonStop)
            return OpeningHours.NonStop();

        var days = new List<DailyOpeningHours>();
        if (Weekdays is not null)
        {
            days.AddRange(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                .Select(d => new DailyOpeningHours(d, Weekdays.Opens, Weekdays.Closes)));
        }

        if (Saturday is not null)
            days.Add(new DailyOpeningHours(DayOfWeek.Saturday, Saturday.Opens, Saturday.Closes));

        if (Sunday is not null)
            days.Add(new DailyOpeningHours(DayOfWeek.Sunday, Sunday.Opens, Sunday.Closes));

        return OpeningHours.Weekly(days);
    }
}
