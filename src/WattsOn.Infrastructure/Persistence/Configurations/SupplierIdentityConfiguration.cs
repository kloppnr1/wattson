using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SupplierIdentityConfiguration : IEntityTypeConfiguration<SupplierIdentity>
{
    public void Configure(EntityTypeBuilder<SupplierIdentity> builder)
    {
        builder.ToTable("supplier_identities");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.OwnsOne(s => s.Gln, gln =>
        {
            gln.Property(g => g.Value).HasColumnName("gln").HasMaxLength(13).IsRequired();
            gln.HasIndex(g => g.Value).IsUnique();
        });

        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.IsActive).HasColumnName("is_active");
        builder.Property(s => s.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);

        builder.OwnsOne(s => s.Cvr, cvr =>
        {
            cvr.Property(c => c.Value).HasColumnName("cvr").HasMaxLength(8);
        });

        builder.Ignore(s => s.DomainEvents);
    }
}
