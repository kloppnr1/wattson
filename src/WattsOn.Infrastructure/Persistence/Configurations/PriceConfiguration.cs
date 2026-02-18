using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class PrisConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.ToTable("prices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Property(p => p.ChargeId).HasColumnName("charge_id").HasMaxLength(50).IsRequired();
        builder.Property(p => p.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(p => p.VatExempt).HasColumnName("vat_exempt");
        builder.Property(p => p.PriceResolution).HasColumnName("price_resolution").HasConversion<string>().HasMaxLength(10);

        builder.OwnsOne(p => p.OwnerGln, gln =>
        {
            gln.Property(g => g.Value).HasColumnName("owner_gln").HasMaxLength(13).IsRequired();
        });

        builder.OwnsOne(p => p.ValidityPeriod, period =>
        {
            period.Property(p => p.Start).HasColumnName("valid_from").IsRequired();
            period.Property(p => p.End).HasColumnName("valid_to");
        });

        builder.HasMany(p => p.PricePoints)
            .WithOne()
            .HasForeignKey(pp => pp.PriceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.ChargeId);

        builder.Ignore(p => p.DomainEvents);
    }
}

public class PrisPointConfiguration : IEntityTypeConfiguration<PrisPoint>
{
    public void Configure(EntityTypeBuilder<PrisPoint> builder)
    {
        builder.ToTable("price_points");

        builder.HasKey(pp => pp.Id);
        builder.Property(pp => pp.Id).HasColumnName("id");
        builder.Property(pp => pp.CreatedAt).HasColumnName("created_at");
        builder.Property(pp => pp.UpdatedAt).HasColumnName("updated_at");

        builder.Property(pp => pp.PriceId).HasColumnName("pris_id").IsRequired();
        builder.Property(pp => pp.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(pp => pp.Price).HasColumnName("price").HasPrecision(18, 6);

        builder.HasIndex(pp => new { pp.PriceId, pp.Timestamp });

        builder.Ignore(pp => pp.DomainEvents);
    }
}
