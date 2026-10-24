using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Api.Contracts.V1;

// Public HTTP contract, version 1. Rules (docs/ai/conventions.md):
//  * additive changes only (new optional fields) inside v1; breaking changes go to Contracts/V2 + /api/v2;
//  * DTOs never expose domain entities, strongly typed ids or value objects (plain Guid/decimal/string);
//  * JSON: camelCase, enums as camelCase strings, unknown request fields are rejected (400).

// ---- Shared ---------------------------------------------------------------------------------------------

/// <summary>Page of results.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount, int TotalPages);

/// <summary>Postal address with coordinates (geocoding is done by the client app).</summary>
/// <param name="Street">Street address, e.g. "350 5th Ave".</param>
/// <param name="City">City, e.g. "New York".</param>
/// <param name="PostalCode">US ZIP code, 5 digits.</param>
/// <param name="Latitude">WGS84 latitude.</param>
/// <param name="Longitude">WGS84 longitude.</param>
public sealed record AddressDto(string Street, string City, string PostalCode, double Latitude, double Longitude);

/// <summary>Partner pharmacy: <c>Code</c> is its stable business identifier (e.g. NYC-MID).</summary>
public sealed record PharmacyDto(
    Guid Id,
    string Code,
    string Name,
    string Street,
    string City,
    string PostalCode,
    double Latitude,
    double Longitude);

/// <summary>Amount with two decimal places and an ISO 4217 currency code (USD).</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

// ---- Auth -----------------------------------------------------------------------------------------------

/// <summary>New customer account; the password must pass the password policy (see <c>Auth:Password</c>).</summary>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

/// <summary>Sign-in with e-mail and password.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Refresh token to exchange for a new token pair (the old one is used up).</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Password change; other sessions of the user are signed out.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Token pair of a session: a short-lived JWT access token and a rotating refresh token.</summary>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid SessionId,
    string TokenType = "Bearer");

/// <summary>Signed-in user; <c>PharmacyId</c> is set for pharmacists, <c>CourierId</c> for couriers.</summary>
public sealed record ProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    Guid? PharmacyId,
    Guid? CourierId,
    IReadOnlyList<AddressDto> Addresses);

/// <summary>Active device session; <c>IsCurrent</c> marks the session of the calling token.</summary>
public sealed record SessionResponse(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool IsCurrent);

// ---- Catalog and cart -----------------------------------------------------------------------------------

/// <summary>Product in search results.</summary>
public sealed record ProductSummaryResponse(Guid Id, string Name, ProductCategory Category, MoneyDto Price);

/// <summary>Product details.</summary>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Ean,
    ProductCategory Category,
    string Description,
    MoneyDto Price,
    bool IsActive);

/// <summary>New quantity of a cart line.</summary>
/// <param name="Quantity">Quantity to set (1–10 by default, see Checkout:MaxQuantityPerItem).</param>
public sealed record SetCartItemRequest(int Quantity);

/// <summary>Cart line priced from the catalog; <c>IsAvailable</c> is false when the product was withdrawn.</summary>
public sealed record CartLineResponse(Guid ProductId, string Name, MoneyDto UnitPrice, int Quantity, MoneyDto LineTotal, bool IsAvailable);

/// <summary>Cart with its total; <c>ExpiresAt</c> is when the cart expires unless it is changed again.</summary>
public sealed record CartResponse(IReadOnlyList<CartLineResponse> Lines, MoneyDto Total, DateTimeOffset? ExpiresAt);

// ---- Checkout, orders, payments -------------------------------------------------------------------------

/// <summary>Checkout of the current cart.</summary>
/// <param name="Address">Delivery address; the pharmacy is selected for it.</param>
public sealed record CheckoutRequest(AddressDto Address);

/// <summary>
/// Created order: the pharmacy that will deliver it, the ETA and the moment the stock reservation expires if the
/// order is not paid.
/// </summary>
public sealed record CheckoutResponse(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    MoneyDto Total,
    PharmacyDto Pharmacy,
    DateTimeOffset EstimatedDeliveryAt,
    DateTimeOffset ReservationExpiresAt);

/// <summary>Order in the customer's order list.</summary>
public sealed record OrderSummaryResponse(
    Guid Id,
    string Number,
    OrderStatus Status,
    MoneyDto Total,
    DateTimeOffset CreatedAt,
    string PharmacyName);

/// <summary>Order line with the name and price captured at checkout.</summary>
public sealed record OrderItemResponse(Guid ProductId, string ProductName, MoneyDto UnitPrice, int Quantity, MoneyDto LineTotal);

/// <summary>One status transition; <c>From</c> is null for the creation of the order.</summary>
public sealed record OrderStatusEntryResponse(OrderStatus? From, OrderStatus To, DateTimeOffset At, string? Reason);

/// <summary>Order details with the fulfilling pharmacy, lines, status history and the courier once assigned.</summary>
public sealed record OrderResponse(
    Guid Id,
    string Number,
    OrderStatus Status,
    MoneyDto Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset EstimatedDeliveryAt,
    DateTimeOffset? DeliveredAt,
    AddressDto DeliveryAddress,
    PharmacyDto Pharmacy,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<OrderStatusEntryResponse> History,
    string? CourierName);

/// <summary>Cancellation by the customer, with a reason kept in the status history.</summary>
public sealed record CancelOrderRequest(string Reason);

/// <summary>Started payment: send the customer to <c>RedirectUrl</c> (the PSP payment page).</summary>
public sealed record PaymentStartResponse(Guid PaymentId, string ProviderPaymentId, string RedirectUrl, MoneyDto Amount);

// ---- Tracking -------------------------------------------------------------------------------------------

/// <summary>Last known courier position.</summary>
public sealed record CourierPositionDto(double Latitude, double Longitude, DateTimeOffset RecordedAt);

/// <summary>Tracking snapshot; <c>Courier</c> is present only while the order is InDelivery.</summary>
public sealed record TrackingResponse(
    Guid OrderId,
    OrderStatus Status,
    PharmacyDto Pharmacy,
    AddressDto Destination,
    CourierPositionDto? Courier,
    DateTimeOffset EstimatedDeliveryAt);

/// <summary>One GPS fix from the courier app.</summary>
/// <param name="Latitude">WGS84 latitude.</param>
/// <param name="Longitude">WGS84 longitude.</param>
/// <param name="RecordedAt">Device time of the fix (UTC).</param>
/// <param name="SpeedKmh">Speed reported by the device, optional.</param>
public sealed record LocationPointRequest(double Latitude, double Longitude, DateTimeOffset RecordedAt, double? SpeedKmh);

/// <summary>Batch of GPS fixes.</summary>
/// <param name="Points">GPS points buffered by the courier app (1–50).</param>
public sealed record LocationBatchRequest(IReadOnlyList<LocationPointRequest> Points);

/// <summary>How many points of the batch were accepted and how many were dropped (future, stale, out of order, outliers).</summary>
public sealed record LocationBatchResponse(int Accepted, int Dropped);

// ---- Pharmacy and courier -------------------------------------------------------------------------------

/// <summary>Order in the pharmacy queue; <c>AcceptDeadline</c> is when an unaccepted order moves to another pharmacy.</summary>
public sealed record PharmacyQueueItemResponse(
    Guid Id,
    string Number,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? AcceptDeadline,
    IReadOnlyList<OrderItemResponse> Items);

/// <summary>Rejection by the pharmacy, with a reason kept in the status history.</summary>
public sealed record RejectOrderRequest(string Reason);

/// <summary>Delivery the courier is working on: where to pick the order up and where to bring it.</summary>
public sealed record CurrentDeliveryResponse(
    Guid DeliveryId,
    Guid OrderId,
    string OrderNumber,
    DeliveryStatus Status,
    PharmacyDto Pharmacy,
    AddressDto Destination);

// ---- Admin ----------------------------------------------------------------------------------------------

/// <summary>New product price in USD; orders already placed keep their price snapshot.</summary>
public sealed record ChangePriceRequest(decimal Price);

/// <summary>Data reset to a seed template.</summary>
/// <param name="Profile">Seed template to load; defaults to Seed:Profile.</param>
public sealed record SeedResetRequest(string? Profile);

/// <summary>Seed template that was loaded and the templates available in this build.</summary>
public sealed record SeedResetResponse(string Profile, IReadOnlyCollection<string> AvailableProfiles);

/// <summary>Change of one property: <c>ChangeType</c> is Added, Modified or Deleted; secrets are shown as <c>***</c>.</summary>
public sealed record AuditChangeResponse(
    string EntityType,
    string EntityId,
    string ChangeType,
    string Property,
    string? OldValue,
    string? NewValue);

/// <summary>Audited API action: who called it, the request, its trace id and every entity change it made.</summary>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid? UserId,
    string? Role,
    string HttpMethod,
    string Path,
    string? TraceId,
    IReadOnlyList<AuditChangeResponse> Changes);
