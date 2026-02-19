using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class AggregatedTimeSeriesConfiguration : IEntityTypeConfiguration<AggregatedTimeSeries>
{
    public void Configure(EntityTypeBuilder<AggregatedTimeSeries> builder)
    {
        builder.ToTable("aggregated_time_series");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.Property(a => a.GridArea).HasColumnName("grid_area").IsRequired();
        builder.Property(a => a.BusinessReason).HasColumnName("business_reason").IsRequired();
        builder.Property(a => a.MeteringPointType).HasColumnName("metering_point_type").IsRequired();
        builder.Property(a => a.SettlementMethod).HasColumnName("settlement_method");

        builder.OwnsOne(a => a.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("period_start").IsRequired();
            period.Property(p => p.End).HasColumnName("period_end");
        });

        builder.Property(a => a.Resolution).HasColumnName("resolution").HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.TotalEnergyKwh).HasColumnName("total_energy_kwh").HasPrecision(18, 3);
        builder.Property(a => a.QualityStatus).HasColumnName("quality_status").IsRequired();
        builder.Property(a => a.ReceivedAt).HasColumnName("received_at");
        builder.Property(a => a.TransactionId).HasColumnName("transaction_id");

        builder.HasMany(a => a.Observations)
            .WithOne()
            .HasForeignKey(o => o.AggregatedTimeSeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Observations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.DomainEvents);
    }
}

public class AggregatedObservationConfiguration : IEntityTypeConfiguration<AggregatedObservation>
{
    public void Configure(EntityTypeBuilder<AggregatedObservation> builder)
    {
        builder.ToTable("aggregated_observations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        builder.Property(o => o.AggregatedTimeSeriesId).HasColumnName("aggregated_time_series_id");
        builder.Property(o => o.Timestamp).HasColumnName("timestamp");
        builder.Property(o => o.Kwh).HasColumnName("kwh").HasPrecision(18, 3);

        builder.Ignore(o => o.DomainEvents);
    }
}
