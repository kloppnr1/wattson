using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SpotPriceConfiguration : IEntityTypeConfiguration<SpotPrice>
{
    public void Configure(EntityTypeBuilder<SpotPrice> builder)
    {
        builder.ToTable("spot_prices");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.Property(s => s.PriceArea).HasColumnName("price_area").HasMaxLength(5).IsRequired();
        builder.Property(s => s.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(s => s.PriceDkkPerKwh).HasColumnName("price_dkk_per_kwh").HasPrecision(18, 6);

        builder.HasIndex(s => new { s.PriceArea, s.Timestamp }).IsUnique();

        builder.Ignore(s => s.DomainEvents);
    }
}
