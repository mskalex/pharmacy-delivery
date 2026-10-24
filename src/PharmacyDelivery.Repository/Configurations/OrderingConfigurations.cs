using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Payments;
using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Repository.Configurations;

internal static class ConcurrencyToken
{
    /// <summary>PostgreSQL system column <c>xmin</c> as optimistic concurrency token (changes on every update).</summary>
    public static void AddXmin<T>(EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string ActiveInPharmacyFilter = "status IN ('AssignedToPharmacy', 'Accepted', 'ReadyForPickup', 'CourierAssigned')";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        ConcurrencyToken.AddXmin(builder);

        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.Property(o => o.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.RejectedPharmacyIds).HasColumnType("uuid[]");
        builder.ComplexProperty(o => o.Total, money =>
        {
            money.Property(m => m.Amount).HasColumnName("total_amount");
            money.Property(m => m.Currency).HasColumnName("total_currency").HasMaxLength(3);
        });

        // Snapshot of the delivery address (jsonb): later edits of saved addresses do not change orders.
        builder.OwnsOne(o => o.DeliveryAddress, address =>
        {
            address.ToJson("delivery_address");
            address.Ignore(a => a.Location);
        });

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<Pharmacy>().WithMany().HasForeignKey(o => o.PharmacyId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.Number).IsUnique().HasDatabaseName("ux_orders_number");

        // Idempotent checkout: one order per (customer, Idempotency-Key).
        builder.HasIndex(o => new { o.CustomerId, o.IdempotencyKey }).IsUnique().HasDatabaseName("ux_orders_customer_idempotency_key");

        // "My orders" with pagination, newest first.
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_orders_customer_created");

        // Pharmacy panel queue: only active orders are indexed.
        builder.HasIndex(o => new { o.PharmacyId, o.Status, o.CreatedAt })
            .HasFilter(ActiveInPharmacyFilter)
            .HasDatabaseName("ix_orders_pharmacy_queue");

        // Acceptance timeout job.
        builder.HasIndex(o => o.AssignedAt)
            .HasFilter("status = 'AssignedToPharmacy'")
            .HasDatabaseName("ix_orders_awaiting_acceptance");

        // Dispatch retry job.
        builder.HasIndex(o => o.UpdatedAt)
            .HasFilter("status = 'ReadyForPickup'")
            .HasDatabaseName("ix_orders_awaiting_dispatch");
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.ComplexProperty(i => i.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("unit_price_amount");
            money.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3);
        });

        // FK index — PostgreSQL does not create indexes for foreign keys by itself.
        builder.HasIndex(i => i.OrderId).HasDatabaseName("ix_order_items_order");
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("order_status_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Reason).HasMaxLength(500);
        builder.HasIndex(h => new { h.OrderId, h.OccurredAt }).HasDatabaseName("ix_order_status_history_order");
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        ConcurrencyToken.AddXmin(builder);
        builder.Property(p => p.Provider).HasMaxLength(32).IsRequired();
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(128).IsRequired();
        builder.ComplexProperty(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount");
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        // Webhook idempotency guaranteed by the database.
        builder.HasIndex(p => new { p.Provider, p.ProviderPaymentId }).IsUnique().HasDatabaseName("ux_payments_provider_payment_id");
        builder.HasIndex(p => p.OrderId).HasDatabaseName("ix_payments_order");
        builder.HasOne<Order>().WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers");
        builder.HasKey(c => c.Id);
        ConcurrencyToken.AddXmin(builder);
        builder.Property(c => c.DisplayName).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.UserId).IsUnique().HasDatabaseName("ux_couriers_user");
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries");
        builder.HasKey(d => d.Id);
        ConcurrencyToken.AddXmin(builder);

        // Invariant "one active delivery per courier" enforced by the database.
        builder.HasIndex(d => d.CourierId)
            .IsUnique()
            .HasFilter("status IN ('Assigned', 'InDelivery')")
            .HasDatabaseName("ux_deliveries_active_courier");

        // One non-cancelled delivery per order.
        builder.HasIndex(d => d.OrderId)
            .IsUnique()
            .HasFilter("status <> 'Cancelled'")
            .HasDatabaseName("ux_deliveries_active_order");

        builder.HasOne<Order>().WithMany().HasForeignKey(d => d.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Courier>().WithMany().HasForeignKey(d => d.CourierId).OnDelete(DeleteBehavior.Restrict);
    }
}
