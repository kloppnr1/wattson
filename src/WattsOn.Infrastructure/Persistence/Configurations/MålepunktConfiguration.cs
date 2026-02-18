using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class MålepunktConfiguration : IEntityTypeConfiguration<Målepunkt>
{
    public void Configure(EntityTypeBuilder<Målepunkt> builder)
    {
        builder.ToTable("målepunkter");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.OwnsOne(m => m.Gsrn, gsrn =>
        {
            gsrn.Property(g => g.Value).HasColumnName("gsrn").HasMaxLength(18).IsRequired();
            gsrn.HasIndex(g => g.Value).IsUnique();
        });

        builder.Property(m => m.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(50);
        builder.Property(m => m.Art).HasColumnName("art").HasConversion<string>().HasMaxLength(50);
        builder.Property(m => m.SettlementMethod).HasColumnName("settlement_method").HasConversion<string>().HasMaxLength(50);
        builder.Property(m => m.Resolution).HasColumnName("resolution").HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.ConnectionState).HasColumnName("connection_state").HasConversion<string>().HasMaxLength(50);
        builder.Property(m => m.GridArea).HasColumnName("grid_area").HasMaxLength(10).IsRequired();
        builder.Property(m => m.HasActiveSupply).HasColumnName("has_active_supply");

        builder.OwnsOne(m => m.GridCompanyGln, gln =>
        {
            gln.Property(g => g.Value).HasColumnName("grid_company_gln").HasMaxLength(13).IsRequired();
        });

        builder.OwnsOne(m => m.Address, addr =>
        {
            addr.Property(a => a.StreetName).HasColumnName("street_name").HasMaxLength(200);
            addr.Property(a => a.BuildingNumber).HasColumnName("building_number").HasMaxLength(10);
            addr.Property(a => a.Floor).HasColumnName("floor").HasMaxLength(10);
            addr.Property(a => a.Suite).HasColumnName("suite").HasMaxLength(10);
            addr.Property(a => a.PostCode).HasColumnName("post_code").HasMaxLength(10);
            addr.Property(a => a.CityName).HasColumnName("city_name").HasMaxLength(100);
            addr.Property(a => a.MunicipalityCode).HasColumnName("municipality_code").HasMaxLength(10);
            addr.Property(a => a.CountryCode).HasColumnName("country_code").HasMaxLength(2);
        });

        builder.HasMany(m => m.Leverancer)
            .WithOne(l => l.Målepunkt)
            .HasForeignKey(l => l.MålepunktId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Tidsserier)
            .WithOne(t => t.Målepunkt)
            .HasForeignKey(t => t.MålepunktId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(m => m.DomainEvents);
    }
}
