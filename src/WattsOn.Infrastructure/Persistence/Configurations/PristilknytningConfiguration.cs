using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class PristilknytningConfiguration : IEntityTypeConfiguration<Pristilknytning>
{
    public void Configure(EntityTypeBuilder<Pristilknytning> builder)
    {
        builder.ToTable("pristilknytninger");

        builder.HasKey(pt => pt.Id);
        builder.Property(pt => pt.Id).HasColumnName("id");
        builder.Property(pt => pt.CreatedAt).HasColumnName("created_at");
        builder.Property(pt => pt.UpdatedAt).HasColumnName("updated_at");

        builder.Property(pt => pt.MålepunktId).HasColumnName("målepunkt_id").IsRequired();
        builder.Property(pt => pt.PrisId).HasColumnName("pris_id").IsRequired();

        builder.OwnsOne(pt => pt.LinkPeriod, period =>
        {
            period.Property(p => p.Start).HasColumnName("link_start").IsRequired();
            period.Property(p => p.End).HasColumnName("link_end");
        });

        builder.HasOne(pt => pt.Målepunkt)
            .WithMany()
            .HasForeignKey(pt => pt.MålepunktId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.Pris)
            .WithMany()
            .HasForeignKey(pt => pt.PrisId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pt => pt.MålepunktId);

        builder.Ignore(pt => pt.DomainEvents);
    }
}
