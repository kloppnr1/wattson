using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class AfregningConfiguration : IEntityTypeConfiguration<Afregning>
{
    public void Configure(EntityTypeBuilder<Afregning> builder)
    {
        builder.ToTable("afregninger");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.Property(a => a.MålepunktId).HasColumnName("målepunkt_id").IsRequired();
        builder.Property(a => a.LeveranceId).HasColumnName("leverance_id").IsRequired();
        builder.Property(a => a.TidsserieId).HasColumnName("tidsserie_id").IsRequired();
        builder.Property(a => a.TidsserieVersion).HasColumnName("tidsserie_version");
        builder.Property(a => a.IsCorrection).HasColumnName("is_correction");
        builder.Property(a => a.PreviousAfregningId).HasColumnName("previous_afregning_id");
        builder.Property(a => a.CalculatedAt).HasColumnName("calculated_at");

        // Document numbering (PostgreSQL sequence — sequential, no gaps under normal operation)
        builder.Property(a => a.DocumentNumber).HasColumnName("document_number")
            .HasDefaultValueSql("nextval('settlement_document_seq')");

        // Invoicing lifecycle
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
        builder.Property(a => a.ExternalInvoiceReference).HasColumnName("external_invoice_reference").HasMaxLength(100);
        builder.Property(a => a.InvoicedAt).HasColumnName("invoiced_at");

        builder.HasIndex(a => a.Status); // Fast lookup for uninvoiced settlements

        builder.OwnsOne(a => a.SettlementPeriod, period =>
        {
            period.Property(p => p.Start).HasColumnName("settlement_start").IsRequired();
            period.Property(p => p.End).HasColumnName("settlement_end");
        });

        builder.OwnsOne(a => a.TotalEnergy, qty =>
        {
            qty.Property(q => q.Value).HasColumnName("total_energy_kwh").HasPrecision(18, 3);
            qty.Property(q => q.Unit).HasColumnName("total_energy_unit").HasMaxLength(5).HasDefaultValue("kWh");
        });

        builder.OwnsOne(a => a.TotalAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("total_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("total_currency").HasMaxLength(3).HasDefaultValue("DKK");
        });

        builder.HasOne(a => a.Målepunkt).WithMany().HasForeignKey(a => a.MålepunktId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Leverance).WithMany().HasForeignKey(a => a.LeveranceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Tidsserie).WithMany().HasForeignKey(a => a.TidsserieId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Lines)
            .WithOne()
            .HasForeignKey(l => l.AfregningId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(a => a.DomainEvents);
    }
}

public class AfregningLinjeConfiguration : IEntityTypeConfiguration<AfregningLinje>
{
    public void Configure(EntityTypeBuilder<AfregningLinje> builder)
    {
        builder.ToTable("afregning_linjer");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder.Property(l => l.AfregningId).HasColumnName("afregning_id").IsRequired();
        builder.Property(l => l.PrisId).HasColumnName("pris_id").IsRequired();
        builder.Property(l => l.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 6);

        builder.OwnsOne(l => l.Quantity, qty =>
        {
            qty.Property(q => q.Value).HasColumnName("quantity_kwh").HasPrecision(18, 3);
            qty.Property(q => q.Unit).HasColumnName("quantity_unit").HasMaxLength(5).HasDefaultValue("kWh");
        });

        builder.OwnsOne(l => l.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("amount_currency").HasMaxLength(3).HasDefaultValue("DKK");
        });

        builder.Ignore(l => l.DomainEvents);
    }
}
