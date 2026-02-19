using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Entities;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class SettlementIssueConfiguration : IEntityTypeConfiguration<SettlementIssue>
{
    public void Configure(EntityTypeBuilder<SettlementIssue> builder)
    {
        builder.ToTable("settlement_issues");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.MeteringPointId).HasColumnName("metering_point_id").IsRequired();
        builder.Property(i => i.TimeSeriesId).HasColumnName("time_series_id").IsRequired();
        builder.Property(i => i.TimeSeriesVersion).HasColumnName("time_series_version").IsRequired();

        builder.OwnsOne(i => i.Period, p =>
        {
            p.Property(x => x.Start).HasColumnName("period_start").IsRequired();
            p.Property(x => x.End).HasColumnName("period_end");
        });

        builder.Property(i => i.IssueType).HasColumnName("issue_type")
            .HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(i => i.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(i => i.Details).HasColumnName("details").HasMaxLength(4000).IsRequired();

        builder.Property(i => i.Status).HasColumnName("status")
            .HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(i => i.ResolvedAt).HasColumnName("resolved_at");

        // Indexes
        builder.HasIndex(i => i.MeteringPointId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => new { i.MeteringPointId, i.TimeSeriesId, i.TimeSeriesVersion }).IsUnique();
    }
}
