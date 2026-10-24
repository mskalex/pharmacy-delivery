using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Resource-based authorization of orders. A user who may not see an order gets 404, not 403,
/// so that order identifiers cannot be probed.
/// </summary>
internal static class OrderAccess
{
    public static bool CanView(OrderAccessInfo order, ICurrentUser user) =>
        user.Role switch
        {
            Roles.Admin => true,
            Roles.Customer => order.CustomerId == user.UserId,
            Roles.Pharmacist => user.PharmacyId == order.PharmacyId,
            Roles.Courier => user.CourierId is { } courierId && order.CourierId == courierId,
            _ => false,
        };

    public static void EnsureCanView(OrderAccessInfo? order, ICurrentUser user, Guid orderId)
    {
        if (order is null || !CanView(order, user))
            throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");
    }
}
