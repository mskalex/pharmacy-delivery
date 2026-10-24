using FluentValidation;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Api.Contracts.V1;

// Level 1 of validation: the shape of the request (DESIGN.md §13). Domain invariants (level 2) and use-case
// rules (level 3) are checked by the domain model and services. Validators run automatically (ValidationFilter).

public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(a => a.Street).NotEmpty().MaximumLength(200);
        RuleFor(a => a.City).NotEmpty().MaximumLength(100);
        RuleFor(a => a.PostalCode).NotEmpty().Matches(@"^\d{5}$").WithMessage("ZIP code must have 5 digits.");
        RuleFor(a => a.Latitude).InclusiveBetween(-90, 90);
        RuleFor(a => a.Longitude).InclusiveBetween(-180, 180);
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(1024); // the password policy is enforced by the service
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(1024);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(1024);
        RuleFor(r => r.NewPassword).NotEmpty().MaximumLength(1024).NotEqual(r => r.CurrentPassword)
            .WithMessage("New password must differ from the current one.");
    }
}

public sealed class SetCartItemRequestValidator : AbstractValidator<SetCartItemRequest>
{
    public SetCartItemRequestValidator(IOptions<CheckoutOptions> options) =>
        RuleFor(r => r.Quantity).InclusiveBetween(1, options.Value.MaxQuantityPerItem);
}

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.Address).NotNull().SetValidator(new AddressDtoValidator());
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
}

public sealed class RejectOrderRequestValidator : AbstractValidator<RejectOrderRequest>
{
    public RejectOrderRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(300);
}

public sealed class LocationBatchRequestValidator : AbstractValidator<LocationBatchRequest>
{
    public LocationBatchRequestValidator(IOptions<TrackingOptions> options)
    {
        RuleFor(r => r.Points).NotNull().NotEmpty();
        RuleFor(r => r.Points.Count).LessThanOrEqualTo(options.Value.MaxBatchSize).When(r => r.Points is not null)
            .WithName("points");
        RuleForEach(r => r.Points).ChildRules(point =>
        {
            point.RuleFor(p => p.Latitude).InclusiveBetween(-90, 90);
            point.RuleFor(p => p.Longitude).InclusiveBetween(-180, 180);
            point.RuleFor(p => p.RecordedAt).NotEqual(default(DateTimeOffset));
            point.RuleFor(p => p.SpeedKmh).InclusiveBetween(0, 300).When(p => p.SpeedKmh is not null);
        });
    }
}

public sealed class ChangePriceRequestValidator : AbstractValidator<ChangePriceRequest>
{
    public ChangePriceRequestValidator() => RuleFor(r => r.Price).GreaterThan(0).LessThanOrEqualTo(100_000).PrecisionScale(12, 2, true);
}

public sealed class SeedResetRequestValidator : AbstractValidator<SeedResetRequest>
{
    public SeedResetRequestValidator() => RuleFor(r => r.Profile).MaximumLength(50).Matches("^[a-zA-Z0-9_-]*$");
}
