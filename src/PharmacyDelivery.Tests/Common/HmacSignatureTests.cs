using PharmacyDelivery.Common.Security;

namespace PharmacyDelivery.Tests.Common;

[TestFixture]
public sealed class HmacSignatureTests : TestBase
{
    private const string Secret = "unit-test-secret-0123456789";

    [Test]
    [Property("TestCase", "TC-7.5.1")]
    public void Verify_AcceptsValidSignature_RejectsTamperedBodyWrongSecretAndOldTimestamp()
    {
        var ts = Now.ToUnixTimeSeconds();
        var signature = HmacSignature.Compute(Secret, ts, "{\"a\":1}");
        var tolerance = TimeSpan.FromMinutes(5);

        Assert.Multiple(() =>
        {
            Assert.That(HmacSignature.Verify(Secret, ts, "{\"a\":1}", signature, Now, tolerance), Is.True);
            Assert.That(HmacSignature.Verify(Secret, ts, "{\"a\":2}", signature, Now, tolerance), Is.False, "tampered body");
            Assert.That(HmacSignature.Verify("other-secret-0123456789", ts, "{\"a\":1}", signature, Now, tolerance), Is.False,
                "wrong secret");
            Assert.That(HmacSignature.Verify(Secret, ts, "{\"a\":1}", signature, Now.AddMinutes(10), tolerance), Is.False,
                "replay of an old request");
            Assert.That(HmacSignature.Verify(Secret, ts, "{\"a\":1}", null, Now, tolerance), Is.False, "missing signature");
        });
    }
}
