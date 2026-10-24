using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Domain.Tracking;
using PharmacyDelivery.Repository.Auditing;

namespace PharmacyDelivery.Repository.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        ConcurrencyToken.AddXmin(builder);
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Role).HasMaxLength(32).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.OwnsMany(u => u.Addresses, a => a.ToJson("addresses"));

        // Case-insensitive login: lookups use the normalized (lower-case) email.
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_email");
    }
}

internal sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserAgent).HasMaxLength(256);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.RevokeReason).HasMaxLength(100);

        // Active sessions list and logout-all.
        builder.HasIndex(s => s.UserId).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_auth_sessions_user_active");
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        ConcurrencyToken.AddXmin(builder); // two parallel refreshes with the same token cannot both succeed
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        builder.HasIndex(t => t.SessionId).HasDatabaseName("ix_refresh_tokens_session");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires");
        builder.HasOne<AuthSession>().WithMany().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccessTokenConfiguration : IEntityTypeConfiguration<AccessToken>
{
    public void Configure(EntityTypeBuilder<AccessToken> builder)
    {
        builder.ToTable("access_tokens");
        builder.HasKey(t => t.Jti);
        builder.HasIndex(t => t.UserId).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_access_tokens_user_active");
        builder.HasIndex(t => t.SessionId).HasDatabaseName("ix_access_tokens_session");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_access_tokens_expires");
        builder.HasOne<AuthSession>().WithMany().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordHistoryConfiguration : IEntityTypeConfiguration<PasswordHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PasswordHistoryEntry> builder)
    {
        builder.ToTable("password_history");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PasswordHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(p => new { p.UserId, p.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_password_history_user");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CourierLocationConfiguration : IEntityTypeConfiguration<CourierLocationRecord>
{
    public void Configure(EntityTypeBuilder<CourierLocationRecord> builder)
    {
        // Partitioned by day (RANGE on recorded_at); created by raw SQL in the migration because EF Core cannot
        // declare partitioned tables. Indexes: BRIN(recorded_at) + B-tree(delivery_id, recorded_at).
        builder.ToTable("courier_locations", t => t.ExcludeFromMigrations());
        builder.HasKey(l => new { l.Id, l.RecordedAt });
        builder.Property(l => l.Id).UseIdentityAlwaysColumn();
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Role).HasMaxLength(32);
        builder.Property(a => a.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(a => a.Path).HasMaxLength(300).IsRequired();
        builder.Property(a => a.TraceId).HasMaxLength(32);
        builder.HasMany(a => a.Changes).WithOne().HasForeignKey(c => c.AuditEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.OccurredAt).HasDatabaseName("ix_audit_entries_occurred");
        builder.HasIndex(a => new { a.UserId, a.OccurredAt }).HasDatabaseName("ix_audit_entries_user");
    }
}

internal sealed class AuditChangeConfiguration : IEntityTypeConfiguration<AuditChange>
{
    public void Configure(EntityTypeBuilder<AuditChange> builder)
    {
        builder.ToTable("audit_changes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityAlwaysColumn();
        builder.Property(c => c.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(c => c.EntityId).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ChangeType).HasMaxLength(10).IsRequired();
        builder.Property(c => c.Property).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.AuditEntryId).HasDatabaseName("ix_audit_changes_entry");

        // "History of entity X".
        builder.HasIndex(c => new { c.EntityType, c.EntityId }).HasDatabaseName("ix_audit_changes_entity");
    }
}
