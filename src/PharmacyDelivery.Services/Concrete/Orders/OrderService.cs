using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

public sealed class OrderService(
    IOrderQueries queries,
    IOrderRepository orders,
    IStockReservationService reservations,
    IPaymentService payments,
    IUnitOfWork unitOfWork,
    IClock clock) : IOrderService
{
    public Task<PagedResult<OrderSummary>> GetMyOrdersAsync(Guid customerId, PageRequest page, CancellationToken cancellationToken) =>
        queries.GetCustomerOrdersAsync(customerId, page, cancellationToken);

    public async Task<OrderDetails> GetDetailsAsync(Guid orderId, ICurrentUser user, CancellationToken cancellationToken)
    {
        OrderAccess.EnsureCanView(await queries.GetAccessInfoAsync(orderId, cancellationToken), user, orderId);
        return await queries.GetDetailsAsync(orderId, cancellationToken)
               ?? throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");
    }

    public async Task<OrderDetails> CancelAsync(Guid orderId, Guid customerId, string reason, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var wasPaid = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await orders.GetForUpdateAsync(new OrderId(orderId), ct);
                if (order is null || order.CustomerId.Value != customerId)
                    throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");

                if (!OrderStateMachine.IsCancellableByCustomer(order.Status))
                {
                    throw new BusinessRuleException(
                        ErrorCodes.OrderInvalidTransition,
                        $"Order {order.Number} can no longer be cancelled (status {order.Status}).");
                }

                var now = clock.UtcNow;
                await reservations.ReleaseForOrderAsync(order.Id, now, ct);
                order.Cancel(reason, now);
                await unitOfWork.SaveChangesAsync(ct);
                return order.WasPaid;
            },
            cancellationToken);

        if (wasPaid)
            await payments.RefundCancelledOrderAsync(new OrderId(orderId), cancellationToken);

        return await GetDetailsAsync(orderId, user, cancellationToken);
    }
}
