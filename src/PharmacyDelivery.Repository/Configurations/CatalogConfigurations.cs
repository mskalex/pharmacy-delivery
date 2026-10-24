using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Repository.Configurations;

// Index rationale for every index below: docs/database.md (query → index → type → why).

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Ean).HasMaxLength(14).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.ComplexProperty(p => p.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("price_amount");
            money.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });

        // Catalog search: name ILIKE '%term%' — trigram GIN index.
        builder.HasIndex(p => p.Name)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_products_name_trgm");

        // Category listing sorted by name (active products only).
        builder.HasIndex(p => new { p.Category, p.Name })
            .HasFilter("is_active")
            .HasDatabaseName("ix_products_category_name_active");

        builder.HasIndex(p => p.Ean).IsUnique().HasDatabaseName("ux_products_ean");
    }
}

internal sealed class PharmacyConfiguration : IEntityTypeConfiguration<Pharmacy>
{
    public void Configure(EntityTypeBuilder<Pharmacy> builder)
    {
        builder.ToTable("pharmacies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Street).HasMaxLength(200).IsRequired();
        builder.Property(p => p.City).HasMaxLength(100).IsRequired();
        builder.Property(p => p.PostalCode).HasMaxLength(6).IsRequired();
        builder.Property(p => p.Location).IsRequired();

        builder.OwnsOne(p => p.OpeningHours, hours =>
        {
            hours.ToJson("opening_hours");
            hours.OwnsMany(h => h.Days);
        });

        builder.HasIndex(p => p.Code).IsUnique().HasDatabaseName("ux_pharmacies_code");

        // ST_DWithin radius search + KNN ordering (location <-> point) for active pharmacies.
        builder.HasIndex(p => p.Location)
            .HasMethod("gist")
            .HasFilter("is_active")
            .HasDatabaseName("ix_pharmacies_location_active");
    }
}

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", table =>
        {
            // Last line of defence of the domain invariant 0 <= reserved <= on_hand.
            table.HasCheckConstraint("ck_stock_items_quantities", "on_hand >= 0 AND reserved >= 0 AND reserved <= on_hand");
        });

        // PK order (pharmacy_id, product_id) matches the lock order of reservations: FOR UPDATE ... ORDER BY product_id.
        builder.HasKey(s => new { s.PharmacyId, s.ProductId });

        // Room for HOT updates: on_hand/reserved change constantly and are intentionally not part of any index.
        builder.HasStorageParameter("fillfactor", 80);

        // "Which pharmacies have every product of the basket".
        builder.HasIndex(s => new { s.ProductId, s.PharmacyId }).HasDatabaseName("ix_stock_items_product_pharmacy");

        builder.HasOne<Pharmacy>().WithMany().HasForeignKey(s => s.PharmacyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");
        builder.HasKey(r => r.Id);

        // Expiry job: only active, expiring reservations are indexed, so the index stays tiny.
        builder.HasIndex(r => r.ExpiresAt)
            .HasFilter("status = 'Active' AND expires_at IS NOT NULL")
            .HasDatabaseName("ix_stock_reservations_expires_active");

        builder.HasIndex(r => r.OrderId).HasDatabaseName("ix_stock_reservations_order");
    }
}
