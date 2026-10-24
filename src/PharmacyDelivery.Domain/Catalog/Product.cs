using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Domain.Catalog;

public enum ProductCategory
{
    PainRelief,
    ColdAndFlu,
    VitaminsAndSupplements,
    Dermocosmetics,
    ForChildren,
    FirstAid,
}

/// <summary>OTC product sold by partner pharmacies. Prescription drugs are out of scope (see DESIGN.md, assumptions).</summary>
public sealed class Product : AggregateRoot<ProductId>
{
    private Product()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Ean { get; private set; } = string.Empty;

    public ProductCategory Category { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public Money Price { get; private set; } = Money.Zero;

    public bool IsActive { get; private set; }

    public static Product Create(
        ProductId id,
        string name,
        string ean,
        ProductCategory category,
        string description,
        Money price)
    {
        Guard.NotBlank(name);
        if (ean.Length is < 8 or > 14 || !ean.All(char.IsAsciiDigit))
            throw new DomainException(ErrorCodes.ValidationFailed, "EAN must contain 8–14 digits.");

        if (price.Amount <= 0)
            throw new DomainException(ErrorCodes.ValidationFailed, "Product price must be positive.");

        return new Product
        {
            Id = id,
            Name = name.Trim(),
            Ean = ean,
            Category = category,
            Description = description.Trim(),
            Price = price,
            IsActive = true,
        };
    }

    public void ChangePrice(Money price)
    {
        if (price.Amount <= 0)
            throw new DomainException(ErrorCodes.ValidationFailed, "Product price must be positive.");

        Price = price;
    }

    public void Deactivate() => IsActive = false;
}
