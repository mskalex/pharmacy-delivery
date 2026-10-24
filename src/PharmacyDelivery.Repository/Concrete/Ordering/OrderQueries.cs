using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class OrderQueries(PharmacyDeliveryDbContext db) : IOrderQueries
{
    public async Task<PagedResult<OrderSummary>> GetCustomerOrdersAsync(Guid customerId, PageRequest page,
        CancellationToken cancellationToken)
    {
        var customer = new UserId(customerId);
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customer);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Join(db.Pharmacies, o => o.PharmacyId, p => p.Id, (o, p) => new { Order = o, PharmacyName = p.Name })
            .Select(x => new OrderSummary(
                x.Order.Id.Value,
                x.Order.Number,
                x.Order.Status,
                x.Order.Total.Amount,
                x.Order.Total.Currency,
                x.Order.CreatedAt,
                x.PharmacyName))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderSummary>(items, page.Page, page.PageSize, total);
    }

    public async Task<OrderDetails?> GetDetailsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var id = new OrderId(orderId);
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
            return null;

        var pharmacy = await db.Pharmacies.AsNoTracking().FirstAsync(p => p.Id == order.PharmacyId, cancellationToken);
        var courier = await db.Deliveries.AsNoTracking()
            .Where(d => d.OrderId == id && d.Status != DeliveryStatus.Cancelled)
            .Join(db.Couriers, d => d.CourierId, c => c.Id, (d, c) => new { c.Id, c.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        return new OrderDetails(
            order.Id.Value,
            order.Number,
            order.CustomerId.Value,
            order.Status,
            order.Total.Amount,
            order.Total.Currency,
            order.CreatedAt,
            order.EstimatedDeliveryAt,
            order.DeliveredAt,
            new AddressInput(order.DeliveryAddress.Street, order.DeliveryAddress.City, order.DeliveryAddress.PostalCode,
                order.DeliveryAddress.Latitude, order.DeliveryAddress.Longitude),
            QueryMapping.ToInfo(pharmacy),
            order.Items.Select(QueryMapping.ToView).ToList(),
            order.History.OrderBy(h => h.OccurredAt).Select(h => new OrderStatusEntry(h.FromStatus, h.ToStatus, h.OccurredAt, h.Reason))
                .ToList(),
            courier?.Id.Value,
            courier?.DisplayName);
    }

    public async Task<OrderAccessInfo?> GetAccessInfoAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var id = new OrderId(orderId);
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new { o.CustomerId, o.PharmacyId, o.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return null;

        var courierId = await db.Deliveries.AsNoTracking()
            .Where(d => d.OrderId == id && d.Status != DeliveryStatus.Cancelled)
            .Select(d => (CourierId?)d.CourierId)
            .FirstOrDefaultAsync(cancellationToken);

        return new OrderAccessInfo(orderId, order.CustomerId.Value, order.PharmacyId.Value, order.Status, courierId?.Value);
    }

    public async Task<IReadOnlyList<PharmacyOrderSummary>> GetPharmacyQueueAsync(Guid pharmacyId, TimeSpan acceptanceTimeout,
        CancellationToken cancellationToken)
    {
        var id = new PharmacyId(pharmacyId);
        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.PharmacyId == id && OrderStateMachine.ActiveInPharmacy.Contains(o.Status))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => new PharmacyOrderSummary(
                o.Id.Value,
                o.Number,
                o.Status,
                o.CreatedAt,
                o.AssignedAt,
                o.Status == OrderStatus.AssignedToPharmacy ? o.AssignedAt?.Add(acceptanceTimeout) : null,
                o.Items.Select(QueryMapping.ToView).ToList()))
            .ToList();
    }
}
