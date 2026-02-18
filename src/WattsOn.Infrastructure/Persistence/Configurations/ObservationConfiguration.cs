using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> builder)
    {
        builder.ToTable("observations");

        // Composite PK: (timestamp, id) — required for TimescaleDB hypertable
        // (all unique constraints must include the partitioning column)
        builder.HasKey(o => new { o.Timestamp, o.Id });
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        builder.Property(o => o.TidsserieId).HasColumnName("tidsserie_id").IsRequired();
        builder.Property(o => o.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(o => o.Quality).HasColumnName("quality").HasConversion<string>().HasMaxLength(30);

        builder.OwnsOne(o => o.Quantity, qty =>
        {
            qty.Property(q => q.Value).HasColumnName("quantity_kwh").HasPrecision(18, 3).IsRequired();
            qty.Property(q => q.Unit).HasColumnName("quantity_unit").HasMaxLength(5).HasDefaultValue("kWh");
        });

        // Index for time-range queries (TimescaleDB will make this a hypertable)
        builder.HasIndex(o => new { o.TidsserieId, o.Timestamp });

        builder.Ignore(o => o.DomainEvents);
    }
}
