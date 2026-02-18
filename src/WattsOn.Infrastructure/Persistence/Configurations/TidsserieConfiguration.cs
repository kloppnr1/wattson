using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class TidsserieConfiguration : IEntityTypeConfiguration<Tidsserie>
{
    public void Configure(EntityTypeBuilder<Tidsserie> builder)
    {
        builder.ToTable("tidsserier");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.Property(t => t.MålepunktId).HasColumnName("målepunkt_id").IsRequired();
        builder.Property(t => t.Resolution).HasColumnName("resolution").HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Version).HasColumnName("version");
        builder.Property(t => t.IsLatest).HasColumnName("is_latest");
        builder.Property(t => t.TransactionId).HasColumnName("transaction_id").HasMaxLength(100);
        builder.Property(t => t.ReceivedAt).HasColumnName("received_at");

        builder.OwnsOne(t => t.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("period_start").IsRequired();
            period.Property(p => p.End).HasColumnName("period_end");
        });

        builder.HasMany(t => t.Observations)
            .WithOne()
            .HasForeignKey(o => o.TidsserieId)
            .OnDelete(DeleteBehavior.Cascade);

        // Composite index: find latest version for a metering point + period
        builder.HasIndex(t => new { t.MålepunktId, t.IsLatest });

        builder.Ignore(t => t.TotalEnergy);
        builder.Ignore(t => t.DomainEvents);
    }
}
