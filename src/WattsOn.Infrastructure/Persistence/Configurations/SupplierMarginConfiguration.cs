using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SupplierMarginConfiguration : IEntityTypeConfiguration<SupplierMargin>
{
    public void Configure(EntityTypeBuilder<SupplierMargin> builder)
    {
        builder.ToTable("supplier_margins");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.Property(m => m.SupplierIdentityId).HasColumnName("supplier_identity_id").IsRequired();
        builder.Property(m => m.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(m => m.PriceDkkPerKwh).HasColumnName("price_dkk_per_kwh").HasPrecision(18, 6);

        builder.HasOne(m => m.SupplierIdentity).WithMany().HasForeignKey(m => m.SupplierIdentityId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.SupplierIdentityId, m.Timestamp }).IsUnique();

        builder.Ignore(m => m.DomainEvents);
    }
}
