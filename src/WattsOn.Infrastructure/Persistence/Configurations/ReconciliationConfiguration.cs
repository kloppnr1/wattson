using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class ReconciliationResultConfiguration : IEntityTypeConfiguration<ReconciliationResult>
{
    public void Configure(EntityTypeBuilder<ReconciliationResult> builder)
    {
        builder.ToTable("reconciliation_results");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.Property(r => r.GridArea).HasColumnName("grid_area").IsRequired().HasMaxLength(10);

        builder.OwnsOne(r => r.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("period_start").IsRequired();
            period.Property(p => p.End).HasColumnName("period_end");
        });

        builder.Property(r => r.OurTotalDkk).HasColumnName("our_total_dkk").HasPrecision(18, 2);
        builder.Property(r => r.DataHubTotalDkk).HasColumnName("datahub_total_dkk").HasPrecision(18, 2);
        builder.Property(r => r.DifferenceDkk).HasColumnName("difference_dkk").HasPrecision(18, 2);
        builder.Property(r => r.DifferencePercent).HasColumnName("difference_percent").HasPrecision(18, 4);
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ReconciliationDate).HasColumnName("reconciliation_date");
        builder.Property(r => r.WholesaleSettlementId).HasColumnName("wholesale_settlement_id");
        builder.Property(r => r.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.ReconciliationResultId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(r => r.DomainEvents);
    }
}

public class ReconciliationLineConfiguration : IEntityTypeConfiguration<ReconciliationLine>
{
    public void Configure(EntityTypeBuilder<ReconciliationLine> builder)
    {
        builder.ToTable("reconciliation_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder.Property(l => l.ReconciliationResultId).HasColumnName("reconciliation_result_id").IsRequired();
        builder.Property(l => l.ChargeId).HasColumnName("charge_id").HasMaxLength(500).IsRequired();
        builder.Property(l => l.ChargeType).HasColumnName("charge_type").HasMaxLength(30).IsRequired();
        builder.Property(l => l.OurAmount).HasColumnName("our_amount").HasPrecision(18, 2);
        builder.Property(l => l.DataHubAmount).HasColumnName("datahub_amount").HasPrecision(18, 2);
        builder.Property(l => l.Difference).HasColumnName("difference").HasPrecision(18, 2);

        builder.Ignore(l => l.DomainEvents);
    }
}
