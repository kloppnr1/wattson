using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class LeveranceConfiguration : IEntityTypeConfiguration<Leverance>
{
    public void Configure(EntityTypeBuilder<Leverance> builder)
    {
        builder.ToTable("leverancer");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder.Property(l => l.MålepunktId).HasColumnName("målepunkt_id").IsRequired();
        builder.Property(l => l.KundeId).HasColumnName("kunde_id").IsRequired();
        builder.Property(l => l.AktørId).HasColumnName("aktør_id").IsRequired();
        builder.Property(l => l.CreatedByProcessId).HasColumnName("created_by_process_id");
        builder.Property(l => l.EndedByProcessId).HasColumnName("ended_by_process_id");

        builder.OwnsOne(l => l.SupplyPeriod, period =>
        {
            period.Property(p => p.Start).HasColumnName("supply_start").IsRequired();
            period.Property(p => p.End).HasColumnName("supply_end");
        });

        builder.HasOne(l => l.Aktør)
            .WithMany()
            .HasForeignKey(l => l.AktørId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(l => l.IsActive);
        builder.Ignore(l => l.DomainEvents);
    }
}
