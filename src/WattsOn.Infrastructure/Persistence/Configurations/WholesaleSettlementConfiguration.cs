using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class WholesaleSettlementConfiguration : IEntityTypeConfiguration<WholesaleSettlement>
{
    public void Configure(EntityTypeBuilder<WholesaleSettlement> builder)
    {
        builder.ToTable("wholesale_settlements");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.Property(a => a.GridArea).HasColumnName("grid_area").IsRequired();
        builder.Property(a => a.BusinessReason).HasColumnName("business_reason").IsRequired();

        builder.OwnsOne(a => a.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("period_start").IsRequired();
            period.Property(p => p.End).HasColumnName("period_end");
        });

        builder.Property(a => a.TotalEnergyKwh).HasColumnName("total_energy_kwh").HasPrecision(18, 3);
        builder.Property(a => a.TotalAmountDkk).HasColumnName("total_amount_dkk").HasPrecision(18, 2);
        builder.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("DKK");
        builder.Property(a => a.Resolution).HasColumnName("resolution").HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.ReceivedAt).HasColumnName("received_at");
        builder.Property(a => a.TransactionId).HasColumnName("transaction_id");

        builder.HasMany(a => a.Lines)
            .WithOne()
            .HasForeignKey(l => l.WholesaleSettlementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.DomainEvents);
    }
}

public class WholesaleSettlementLineConfiguration : IEntityTypeConfiguration<WholesaleSettlementLine>
{
    public void Configure(EntityTypeBuilder<WholesaleSettlementLine> builder)
    {
        builder.ToTable("wholesale_settlement_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder.Property(l => l.WholesaleSettlementId).HasColumnName("wholesale_settlement_id").IsRequired();
        builder.Property(l => l.ChargeId).HasColumnName("charge_id").HasMaxLength(50).IsRequired();
        builder.Property(l => l.ChargeType).HasColumnName("charge_type").HasMaxLength(30).IsRequired();
        builder.Property(l => l.OwnerGln).HasColumnName("owner_gln").HasMaxLength(13).IsRequired();
        builder.Property(l => l.EnergyKwh).HasColumnName("energy_kwh").HasPrecision(18, 3);
        builder.Property(l => l.AmountDkk).HasColumnName("amount_dkk").HasPrecision(18, 2);
        builder.Property(l => l.Description).HasColumnName("description").HasMaxLength(500);

        builder.Ignore(l => l.DomainEvents);
    }
}
