using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SpotPriceConfiguration : IEntityTypeConfiguration<SpotPrice>
{
    public void Configure(EntityTypeBuilder<SpotPrice> builder)
    {
        builder.ToTable("spot_prices");

        builder.HasKey(sp => sp.Id);

        builder.Property(sp => sp.HourUtc).IsRequired();
        builder.Property(sp => sp.HourDk).IsRequired();
        builder.Property(sp => sp.PriceArea).HasMaxLength(10).IsRequired();
        builder.Property(sp => sp.SpotPriceDkkPerMwh).HasPrecision(18, 6).IsRequired();
        builder.Property(sp => sp.SpotPriceEurPerMwh).HasPrecision(18, 6).IsRequired();

        // Unique constraint: one price per hour per area
        builder.HasIndex(sp => new { sp.HourUtc, sp.PriceArea }).IsUnique();

        // Index for querying by area and time range
        builder.HasIndex(sp => new { sp.PriceArea, sp.HourUtc });
    }
}
