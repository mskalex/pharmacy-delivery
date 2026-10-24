using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Services;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Tests.Domain;

[TestFixture]
public sealed class PharmacyTests : TestBase
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static Pharmacy Create(OpeningHours hours) =>
        Pharmacy.Create(PharmacyId.New(), "T-1", "Test Pharmacy", "1 Test Street", "New York", "10001", new GeoPoint(40.75, -73.99), hours);

    [Test]
    [Property("TestCase", "TC-7.3.2")]
    public void OpeningHours_AreEvaluatedInNewYorkTime()
    {
        var pharmacy = Create(OpeningHours.Weekly([new DailyOpeningHours(DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(20, 0))]));

        // 2026-10-21 is a Wednesday; New York is UTC-4 (EDT) on that date.
        Assert.Multiple(() =>
        {
            Assert.That(pharmacy.IsOpenAt(new DateTimeOffset(2026, 10, 21, 12, 30, 0, TimeSpan.Zero), NewYork), Is.True, "08:30 local");
            Assert.That(pharmacy.IsOpenAt(new DateTimeOffset(2026, 10, 21, 11, 30, 0, TimeSpan.Zero), NewYork), Is.False, "07:30 local");
            Assert.That(pharmacy.IsOpenAt(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), NewYork), Is.False,
                "20:00 local, closing time");
            Assert.That(Create(OpeningHours.NonStop()).IsOpenAt(new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero), NewYork), Is.True,
                "24/7");
        });
    }

    [Test]
    [Property("TestCase", "TC-7.3.3")]
    public void RankingPolicy_PrefersFasterDeliveryOverPureDistance_AndSkipsExcluded()
    {
        var near = new PharmacyCandidate(PharmacyId.New(), "near but busy", 1.0, TimeSpan.FromMinutes(4), ActiveOrders: 10);
        var far = new PharmacyCandidate(PharmacyId.New(), "far but idle", 3.0, TimeSpan.FromMinutes(10), ActiveOrders: 0);
        var excluded = new PharmacyCandidate(PharmacyId.New(), "excluded", 0.5, TimeSpan.FromMinutes(2), ActiveOrders: 0);

        var ranked = PharmacyRankingPolicy.Default.Rank([near, far, excluded], [excluded.PharmacyId.Value]);

        Assert.That(ranked.Select(r => r.Name), Is.EqualTo(new[] { "far but idle", "near but busy" }));
    }
}
