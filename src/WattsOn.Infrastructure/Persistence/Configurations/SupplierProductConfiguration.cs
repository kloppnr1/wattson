using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> builder)
    {
        builder.ToTable("supplier_products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Property(p => p.SupplierIdentityId).HasColumnName("supplier_identity_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(p => p.PricingModel).HasColumnName("pricing_model")
            .HasConversion<string>().HasMaxLength(20).HasDefaultValue(Domain.Enums.PricingModel.SpotAddon);
        builder.Property(p => p.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.HasOne(p => p.SupplierIdentity).WithMany().HasForeignKey(p => p.SupplierIdentityId).OnDelete(DeleteBehavior.Restrict);

        // Product name must be unique per supplier identity
        builder.HasIndex(p => new { p.SupplierIdentityId, p.Name }).IsUnique();

        builder.Ignore(p => p.DomainEvents);
    }
}
