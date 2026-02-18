using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class ActorConfiguration : IEntityTypeConfiguration<Actor>
{
    public void Configure(EntityTypeBuilder<Actor> builder)
    {
        builder.ToTable("actors");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.OwnsOne(a => a.Gln, gln =>
        {
            gln.Property(g => g.Value).HasColumnName("gln").HasMaxLength(13).IsRequired();
            gln.HasIndex(g => g.Value).IsUnique();
        });

        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(a => a.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(50);
        builder.Property(a => a.IsOwn).HasColumnName("is_own");

        builder.OwnsOne(a => a.Cvr, cvr =>
        {
            cvr.Property(c => c.Value).HasColumnName("cvr").HasMaxLength(8);
        });

        builder.Ignore(a => a.DomainEvents);
    }
}
