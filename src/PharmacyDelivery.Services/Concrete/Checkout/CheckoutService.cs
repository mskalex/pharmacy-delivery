using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Checkout = select pharmacy + reserve stock + create order, see DESIGN.md §6 and §10.
/// Each attempt runs in its own short transaction; if the best pharmacy cannot reserve (stock taken by a
/// concurrent checkout or lock timeout), the next ranked pharmacy is tried.
/// </summary>
public sealed class CheckoutService(
    ICartStore cartStore,
    ICatalogQueries catalog,
    IPharmacySelector pharmacySelector,
    IStockReservationService reservations,
    IOrderRepository orders,
    IPharmacyRepository pharmacies,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<CheckoutOptions> options,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private const string NumberAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly CheckoutOptions _options = options.Value;

    public async Task<CheckoutResult> CheckoutAsync(
        Guid customerId,
        string idempotencyKey,
        AddressInput address,
        CancellationToken cancellationToken)
    {
        using var activity = PharmacyDeliveryTelemetry.StartActivity("checkout");
        var stopwatch = Stopwatch.StartNew();
        var customer = new UserId(customerId);

        var existing = await orders.FindByIdempotencyKeyAsync(customer, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            activity?.SetTag("result", "replay");
            return await ToResultAsync(existing, isReplay: true, cancellationToken);
        }

        var lines = await LoadLinesAsync(customerId, cancellationToken);
        var destination = new GeoPoint(address.Latitude, address.Longitude);
        var deliveryAddress = new DeliveryAddress(address.Street, address.City, address.PostalCode, address.Latitude, address.Longitude);
        var now = clock.UtcNow;
        var quantities = lines.Select(l => (l.ProductId, l.Quantity)).ToList();

        var selection = await pharmacySelector.SelectAsync(destination, quantities, [], now, cancellationToken);
        if (selection.Ranked.Count == 0)
        {
            activity?.SetTag("result", "no_pharmacy");
            throw selection.PharmaciesInRange == 0
                ? new BusinessRuleException(ErrorCodes.DeliveryOutOfZone, "The address is outside the delivery zone.")
                : new BusinessRuleException(ErrorCodes.NoPharmacyAvailable, "No open pharmacy in range can fulfil the whole basket.");
        }

        AppException? lastError = null;
        foreach (var ranked in selection.Ranked.Take(_options.MaxPharmacyAttempts))
        {
            try
            {
                var order = await unitOfWork.ExecuteInTransactionAsync(
                    async ct =>
                    {
                        var orderId = OrderId.New();
                        var expiresAt = now.AddMinutes(_options.ReservationTtlMinutes);
                        await reservations.ReserveAsync(orderId, ranked.Pharmacy.Id, quantities, now, expiresAt, ct);

                        var created = Order.Create(
                            orderId,
                            NewOrderNumber(now),
                            customer,
                            ranked.Pharmacy.Id,
                            deliveryAddress,
                            lines,
                            idempotencyKey,
                            now,
                            now.Add(ranked.EstimatedDelivery));
                        orders.Add(created);
                        await unitOfWork.SaveChangesAsync(ct);
                        return created;
                    },
                    cancellationToken);

                await cartStore.DeleteAsync(customerId, cancellationToken);
                PharmacyDeliveryTelemetry.OrdersCreated.Add(1);
                PharmacyDeliveryTelemetry.CheckoutDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                activity?.SetTag("result", "created");
                activity?.SetTag("order.id", order.Id.Value);
                activity?.SetTag("pharmacy.id", order.PharmacyId.Value);
                logger.LogInformation("Order {OrderNumber} created in pharmacy {PharmacyId}", order.Number, order.PharmacyId);
                return Map(order, ranked.Pharmacy, isReplay: false, now.AddMinutes(_options.ReservationTtlMinutes));
            }
            catch (DomainException ex) when (ex.Code == ErrorCodes.StockInsufficient)
            {
                PharmacyDeliveryTelemetry.ReservationConflicts.Add(1, new KeyValuePair<string, object?>("reason", "insufficient"));
                logger.LogInformation("Pharmacy {PharmacyId} cannot reserve the basket: {Reason}", ranked.Pharmacy.Id, ex.Message);
                lastError = new BusinessRuleException(ex.Code, "Some products are no longer available in the selected pharmacies.");
            }
            catch (ConflictException ex) when (ex.Code == ErrorCodes.StockBusy)
            {
                PharmacyDeliveryTelemetry.ReservationConflicts.Add(1, new KeyValuePair<string, object?>("reason", "busy"));
                logger.LogWarning("Stock rows of pharmacy {PharmacyId} are busy (lock timeout)", ranked.Pharmacy.Id);
                lastError = ex;
            }
            catch (ConflictException ex) when (ex.Code == ErrorCodes.IdempotencyKeyReused)
            {
                // A concurrent request with the same idempotency key won the race: return its order.
                var winner = await orders.FindByIdempotencyKeyAsync(customer, idempotencyKey, cancellationToken)
                             ?? throw new ConflictException(ex.Code, ex.Message, ex);
                return await ToResultAsync(winner, isReplay: true, cancellationToken);
            }
        }

        activity?.SetTag("result", "failed");
        throw lastError ?? new BusinessRuleException(ErrorCodes.NoPharmacyAvailable, "No pharmacy can fulfil the basket.");
    }

    private async Task<IReadOnlyList<OrderLine>> LoadLinesAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var cart = await cartStore.GetAsync(customerId, cancellationToken);
        if (cart is null || cart.Items.Count == 0)
            throw new BusinessRuleException(ErrorCodes.CartEmpty, "The cart is empty.");

        var products = (await catalog.GetManyAsync(cart.Items.Select(i => i.ProductId).ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        return cart.Items
            .Select(item =>
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                    throw new BusinessRuleException(ErrorCodes.ProductNotFound, $"Product {item.ProductId} no longer exists.");

                if (!product.IsActive)
                    throw new BusinessRuleException(ErrorCodes.ProductInactive, $"Product {product.Name} is no longer sold.");

                return new OrderLine(new ProductId(product.Id), product.Name, Money.OfUsd(product.Price), item.Quantity);
            })
            .ToList();
    }

    private async Task<CheckoutResult> ToResultAsync(Order order, bool isReplay, CancellationToken cancellationToken)
    {
        var pharmacy = await pharmacies.GetAsync(order.PharmacyId, cancellationToken)
                       ?? throw new NotFoundException(ErrorCodes.PharmacyNotFound, $"Pharmacy {order.PharmacyId} was not found.");
        return Map(order, pharmacy, isReplay, order.CreatedAt.AddMinutes(_options.ReservationTtlMinutes));
    }

    private static CheckoutResult Map(Order order, Pharmacy pharmacy, bool isReplay, DateTimeOffset reservationExpiresAt) =>
        new(
            order.Id.Value,
            order.Number,
            order.Status,
            order.Total.Amount,
            order.Total.Currency,
            PharmacyMapping.ToInfo(pharmacy),
            order.EstimatedDeliveryAt,
            reservationExpiresAt,
            isReplay);

    private static string NewOrderNumber(DateTimeOffset now)
    {
        Span<char> suffix = stackalloc char[6];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = NumberAlphabet[RandomNumberGenerator.GetInt32(NumberAlphabet.Length)];
        }

        return $"PD-{now:yyMMdd}-{suffix}";
    }
}
