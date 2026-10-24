using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Tests.Services;

[TestFixture]
public sealed class PasswordPolicyValidatorTests : TestBase
{
    private PasswordPolicyValidator Validator(params (string Key, string Value)[] settings)
    {
        foreach (var (key, value) in settings)
            Settings["Auth:Password:" + key] = value;

        return Service<PasswordPolicyValidator>();
    }

    [TestCase("Short1!", ErrorCodes.PasswordTooShort, TestName = "TooShort")]
    [TestCase("Unbelievable", ErrorCodes.PasswordCommon, TestName = "CommonPassword")]
    [TestCase("emily.johnson-secret", ErrorCodes.PasswordContainsPersonalData, TestName = "ContainsEmail")]
    [TestCase("My-Favorite-Pharmacy-2026", ErrorCodes.PasswordContainsPersonalData, TestName = "ContainsServiceName")]
    [Property("TestCase", "TC-7.11.1")]
    public void Validate_RejectsWeakPasswords(string password, string expectedCode)
    {
        var violations = Validator().Validate(password, "emily.johnson@example.com", "Emily Johnson");

        Assert.That(violations.Select(v => v.Code), Does.Contain(expectedCode));
    }

    [Test]
    [Property("TestCase", "TC-7.11.2")]
    public void Validate_AcceptsLongPassphraseWithoutCompositionRules()
    {
        Assert.That(Validator().Validate("green bicycle rides along the river", "david@example.com", "David Cohen"), Is.Empty);
    }

    [Test]
    [Property("TestCase", "TC-7.11.3")]
    public void Validate_CompositionRulesAreConfigurable_AndLengthCountsUnicodeCharacters()
    {
        var strict = Validator(("RequireDigit", "true"), ("RequireUppercase", "true"));

        Assert.Multiple(() =>
        {
            Assert.That(strict.Validate("a very long password without digits", null, null).Select(v => v.Code),
                Does.Contain(ErrorCodes.PasswordComposition));
            Assert.That(new PasswordPolicyValidator(OptionsOf(new AuthOptions())).Validate("שלוםשלוםשלום", null, null), Is.Empty,
                "12 Hebrew letters = 12 characters");
            Assert.That(PasswordPolicyValidator.Normalize("ﬁ"), Is.EqualTo("fi"), "NFKC normalization");
        });
    }
}
