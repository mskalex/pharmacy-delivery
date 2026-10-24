using PharmacyDelivery.Common.Geo;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Common.Security;

namespace PharmacyDelivery.Tests.Common;

[TestFixture]
public sealed class CommonUtilitiesTests : TestBase
{
    [Test]
    [Property("TestCase", "TC-7.3.1")]
    public void Haversine_DistanceMidtownToUpperEastSide_IsAboutFourKilometres()
    {
        var km = Haversine.DistanceKm(40.7476, -73.9881, 40.7772, -73.9558);

        Assert.That(km, Is.InRange(4.1, 4.4));
    }

    [Test]
    public void PageRequest_ClampsValues()
    {
        var page = new PageRequest(-3, 10_000);

        Assert.That((page.Page, page.PageSize, page.Skip), Is.EqualTo((1, PageRequest.MaxPageSize, 0)));
    }

    [Test]
    public void TokenHashing_IsDeterministicAndTokensAreUnique()
    {
        Assert.That(TokenHashing.Sha256Hex("abc"), Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        var first = TokenHashing.GenerateOpaqueToken();
        var second = TokenHashing.GenerateOpaqueToken();
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void Guard_RejectsInvalidArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Guard.NotBlank(" "), Throws.ArgumentException);
            Assert.That(() => Guard.Positive(0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Guard.NotEmpty(Guid.Empty), Throws.ArgumentException);
            Assert.That(Guard.NotNegative(0), Is.Zero);
        });
    }
}
