using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasColumnName("id");
        builder.Property(k => k.CreatedAt).HasColumnName("created_at");
        builder.Property(k => k.UpdatedAt).HasColumnName("updated_at");

        builder.Property(k => k.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(k => k.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(k => k.Phone).HasColumnName("phone").HasMaxLength(20);

        builder.OwnsOne(k => k.Cpr, cpr =>
        {
            cpr.Property(c => c.Value).HasColumnName("cpr").HasMaxLength(10);
        });

        builder.OwnsOne(k => k.Cvr, cvr =>
        {
            cvr.Property(c => c.Value).HasColumnName("cvr").HasMaxLength(8);
        });

        builder.OwnsOne(k => k.Address, addr =>
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

        builder.HasMany(k => k.Supplies)
            .WithOne(l => l.Customer)
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(k => k.DomainEvents);
    }
}
