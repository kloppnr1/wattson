using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SupplyProductPeriodConfiguration : IEntityTypeConfiguration<SupplyProductPeriod>
{
    public void Configure(EntityTypeBuilder<SupplyProductPeriod> builder)
    {
        builder.ToTable("supply_product_periods");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Property(p => p.SupplyId).HasColumnName("supply_id").IsRequired();
        builder.Property(p => p.SupplierProductId).HasColumnName("supplier_product_id").IsRequired();

        builder.OwnsOne(p => p.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("period_start").IsRequired();
            period.Property(p => p.End).HasColumnName("period_end");
        });

        builder.HasOne(p => p.Supply).WithMany().HasForeignKey(p => p.SupplyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.SupplierProduct).WithMany().HasForeignKey(p => p.SupplierProductId).OnDelete(DeleteBehavior.Restrict);

        // Index for looking up active product on a supply at a given time
        builder.HasIndex(p => new { p.SupplyId, p.SupplierProductId });

        builder.Ignore(p => p.DomainEvents);
    }
}
