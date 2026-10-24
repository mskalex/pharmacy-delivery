using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Api.Contracts.V1;

/// <summary>Explicit mapping service models → v1 DTOs (no AutoMapper: explicit code is easier to review).</summary>
public static class V1Mapping
{
    public static MoneyDto Money(decimal amount, string currency) => new(amount, currency);

    public static AddressDto ToDto(this AddressInput a) => new(a.Street, a.City, a.PostalCode, a.Latitude, a.Longitude);

    public static AddressInput ToInput(this AddressDto a) =>
        new(a.Street.Trim(), a.City.Trim(), a.PostalCode.Trim(), a.Latitude, a.Longitude);

    public static PharmacyDto ToDto(this PharmacyInfo p) =>
        new(p.Id, p.Code, p.Name, p.Street, p.City, p.PostalCode, p.Latitude, p.Longitude);

    public static PagedResponse<TOut> ToResponse<TIn, TOut>(this PagedResult<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);

    public static TokenResponse ToResponse(this AuthTokens t) =>
        new(t.AccessToken, t.AccessTokenExpiresAt, t.RefreshToken, t.RefreshTokenExpiresAt, t.SessionId);

    public static ProfileResponse ToResponse(this UserProfile p) =>
        new(p.Id, p.Email, p.DisplayName, p.Role, p.PharmacyId, p.CourierId, p.Addresses.Select(ToDto).ToList());

    public static SessionResponse ToResponse(this SessionInfo s) =>
        new(s.Id, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastUsedAt, s.IsCurrent);

    public static ProductSummaryResponse ToResponse(this ProductSummary p) =>
        new(p.Id, p.Name, p.Category, Money(p.Price, p.Currency));

    public static ProductResponse ToResponse(this ProductDetails p) =>
        new(p.Id, p.Name, p.Ean, p.Category, p.Description, Money(p.Price, p.Currency), p.IsActive);

    public static CartResponse ToResponse(this CartView c) =>
        new(
            c.Lines.Select(l => new CartLineResponse(l.ProductId, l.Name, Money(l.UnitPrice, c.Currency), l.Quantity,
                Money(l.LineTotal, c.Currency), l.IsAvailable)).ToList(),
            Money(c.Total, c.Currency),
            c.ExpiresAt);

    public static CheckoutResponse ToResponse(this CheckoutResult r) =>
        new(r.OrderId, r.OrderNumber, r.Status, Money(r.Total, r.Currency), r.Pharmacy.ToDto(), r.EstimatedDeliveryAt,
            r.ReservationExpiresAt);

    public static OrderSummaryResponse ToResponse(this OrderSummary o) =>
        new(o.Id, o.Number, o.Status, Money(o.Total, o.Currency), o.CreatedAt, o.PharmacyName);

    public static OrderItemResponse ToResponse(this OrderItemView i, string currency) =>
        new(i.ProductId, i.ProductName, Money(i.UnitPrice, currency), i.Quantity, Money(i.LineTotal, currency));

    public static OrderResponse ToResponse(this OrderDetails o) =>
        new(
            o.Id,
            o.Number,
            o.Status,
            Money(o.Total, o.Currency),
            o.CreatedAt,
            o.EstimatedDeliveryAt,
            o.DeliveredAt,
            o.DeliveryAddress.ToDto(),
            o.Pharmacy.ToDto(),
            o.Items.Select(i => i.ToResponse(o.Currency)).ToList(),
            o.History.Select(h => new OrderStatusEntryResponse(h.From, h.To, h.At, h.Reason)).ToList(),
            o.CourierName);

    public static PaymentStartResponse ToResponse(this PaymentStartResult p) =>
        new(p.PaymentId, p.ProviderPaymentId, p.RedirectUrl, Money(p.Amount, p.Currency));

    public static TrackingResponse ToResponse(this TrackingSnapshot t) =>
        new(
            t.OrderId,
            t.Status,
            t.Pharmacy.ToDto(),
            t.Destination.ToDto(),
            t.Courier is null ? null : new CourierPositionDto(t.Courier.Latitude, t.Courier.Longitude, t.Courier.RecordedAt),
            t.EstimatedDeliveryAt);

    public static LocationPointInput ToInput(this LocationPointRequest p) => new(p.Latitude, p.Longitude, p.RecordedAt, p.SpeedKmh);

    public static PharmacyQueueItemResponse ToResponse(this PharmacyOrderSummary o) =>
        new(o.Id, o.Number, o.Status, o.CreatedAt, o.AssignedAt, o.AcceptDeadline,
            o.Items.Select(i => i.ToResponse(Domain.Shared.Money.Usd)).ToList());

    public static CurrentDeliveryResponse ToResponse(this CurrentDeliveryInfo d) =>
        new(d.DeliveryId, d.OrderId, d.OrderNumber, d.Status, d.Pharmacy.ToDto(), d.Destination.ToDto());

    public static AuditEntryResponse ToResponse(this AuditEntryView e) =>
        new(
            e.Id,
            e.OccurredAt,
            e.Action,
            e.UserId,
            e.Role,
            e.HttpMethod,
            e.Path,
            e.TraceId,
            e.Changes.Select(c => new AuditChangeResponse(c.EntityType, c.EntityId, c.ChangeType, c.Property, c.OldValue, c.NewValue))
                .ToList());
}
