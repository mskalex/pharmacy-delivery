namespace PharmacyDelivery.Common.Errors;

/// <summary>
/// Catalog of machine-readable error codes returned in ProblemDetails (<c>code</c> extension).
/// Clients localize messages by code; codes are part of the public API contract and never change meaning.
/// </summary>
public static class ErrorCodes
{
    // Generic
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "resource.not_found";
    public const string Forbidden = "resource.forbidden";
    public const string Unauthorized = "auth.unauthorized";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string Internal = "server.error";
    public const string ServiceUnavailable = "server.unavailable";
    public const string DatabaseTimeout = "database.timeout";
    public const string DatabaseConstraint = "database.constraint_violation";
    public const string RateLimited = "request.rate_limited";

    // Catalog and cart
    public const string ProductNotFound = "product.not_found";
    public const string ProductInactive = "product.inactive";
    public const string CartEmpty = "cart.empty";
    public const string CartTooManyItems = "cart.too_many_items";
    public const string CartQuantityOutOfRange = "cart.quantity_out_of_range";

    // Checkout and stock
    public const string StockInsufficient = "stock.insufficient";
    public const string StockBusy = "stock.busy";
    public const string StockInvariantViolated = "stock.invariant_violated";
    public const string NoPharmacyAvailable = "checkout.no_pharmacy_available";
    public const string DeliveryOutOfZone = "delivery.out_of_zone";
    public const string IdempotencyKeyReused = "checkout.idempotency_key_reused";

    // Orders
    public const string OrderNotFound = "order.not_found";
    public const string OrderInvalidTransition = "order.invalid_transition";
    public const string OrderNotTrackable = "order.not_trackable";

    // Payments
    public const string PaymentDuplicate = "payment.duplicate";
    public const string PaymentInvalidSignature = "payment.invalid_signature";
    public const string PaymentNotFound = "payment.not_found";
    public const string PaymentAmountMismatch = "payment.amount_mismatch";

    // Pharmacies, couriers, deliveries
    public const string PharmacyNotFound = "pharmacy.not_found";
    public const string CourierNotFound = "courier.not_found";
    public const string CourierBusy = "delivery.courier_busy";
    public const string DeliveryNotFound = "delivery.not_found";
    public const string DeliveryInvalidTransition = "delivery.invalid_transition";
    public const string DeliveryDuplicate = "delivery.duplicate";

    // Auth and users
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string AccountLocked = "auth.account_locked";
    public const string AccountBlocked = "auth.account_blocked";
    public const string RefreshTokenInvalid = "auth.refresh_token_invalid";
    public const string RefreshTokenReused = "auth.refresh_token_reused";
    public const string SessionNotFound = "auth.session_not_found";
    public const string EmailTaken = "user.email_taken";
    public const string UserNotFound = "user.not_found";

    // Password policy
    public const string PasswordTooShort = "password.too_short";
    public const string PasswordTooLong = "password.too_long";
    public const string PasswordCommon = "password.common";
    public const string PasswordContainsPersonalData = "password.contains_personal_data";
    public const string PasswordReused = "password.reused";
    public const string PasswordComposition = "password.composition";
    public const string PasswordCurrentInvalid = "password.current_invalid";

    // Maintenance
    public const string MaintenanceDisabled = "maintenance.disabled";
}
