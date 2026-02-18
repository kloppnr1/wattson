using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class PriceLinkConfiguration : IEntityTypeConfiguration<PriceLink>
{
    public void Configure(EntityTypeBuilder<PriceLink> builder)
    {
        builder.ToTable("price_links");

        builder.HasKey(pt => pt.Id);
        builder.Property(pt => pt.Id).HasColumnName("id");
        builder.Property(pt => pt.CreatedAt).HasColumnName("created_at");
        builder.Property(pt => pt.UpdatedAt).HasColumnName("updated_at");

        builder.Property(pt => pt.MeteringPointId).HasColumnName("metering_point_id").IsRequired();
        builder.Property(pt => pt.PriceId).HasColumnName("pris_id").IsRequired();

        builder.OwnsOne(pt => pt.LinkPeriod, period =>
        {
            period.Property(p => p.Start).HasColumnName("link_start").IsRequired();
            period.Property(p => p.End).HasColumnName("link_end");
        });

        builder.HasOne(pt => pt.MeteringPoint)
            .WithMany()
            .HasForeignKey(pt => pt.MeteringPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.Price)
            .WithMany()
            .HasForeignKey(pt => pt.PriceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pt => pt.MeteringPointId);

        builder.Ignore(pt => pt.DomainEvents);
    }
}
