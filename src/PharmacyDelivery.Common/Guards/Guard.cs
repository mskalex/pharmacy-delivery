using System.Runtime.CompilerServices;

namespace PharmacyDelivery.Common.Guards;

/// <summary>Argument checks for programming errors. Business rule violations use domain exceptions instead.</summary>
public static class Guard
{
    public static string NotBlank(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", name);

        return value;
    }

    public static int Positive(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");

        return value;
    }

    public static int NotNegative(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must not be negative.");

        return value;
    }

    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identifier must not be empty.", name);

        return value;
    }
}
