namespace PharmacyDelivery.Infrastructure.Seed;

internal sealed record UsersJson(PersonJson Admin, IReadOnlyList<PersonJson> Customers, IReadOnlyList<PersonJson> Couriers);
