using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Processes;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class BrsProcessConfiguration : IEntityTypeConfiguration<BrsProcess>
{
    public void Configure(EntityTypeBuilder<BrsProcess> builder)
    {
        builder.ToTable("brs_processes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Property(p => p.TransactionId).HasColumnName("transaction_id").HasMaxLength(100);
        builder.Property(p => p.ProcessType).HasColumnName("process_type").HasConversion<string>().HasMaxLength(50);
        builder.Property(p => p.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.CurrentState).HasColumnName("current_state").HasMaxLength(100);
        builder.Property(p => p.EffectiveDate).HasColumnName("effective_date");
        builder.Property(p => p.StartedAt).HasColumnName("started_at");
        builder.Property(p => p.CompletedAt).HasColumnName("completed_at");
        builder.Property(p => p.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        builder.Property(p => p.ProcessData).HasColumnName("process_data").HasColumnType("jsonb");

        builder.OwnsOne(p => p.MålepunktGsrn, gsrn =>
        {
            gsrn.Property(g => g.Value).HasColumnName("målepunkt_gsrn").HasMaxLength(18);
        });

        builder.OwnsOne(p => p.CounterpartGln, gln =>
        {
            gln.Property(g => g.Value).HasColumnName("counterpart_gln").HasMaxLength(13);
        });

        builder.HasMany(p => p.Transitions)
            .WithOne()
            .HasForeignKey(t => t.ProcessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.TransactionId);
        builder.HasIndex(p => p.Status);

        builder.Ignore(p => p.DomainEvents);
    }
}

public class ProcessStateTransitionConfiguration : IEntityTypeConfiguration<ProcessStateTransition>
{
    public void Configure(EntityTypeBuilder<ProcessStateTransition> builder)
    {
        builder.ToTable("process_state_transitions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.Property(t => t.ProcessId).HasColumnName("process_id").IsRequired();
        builder.Property(t => t.FromState).HasColumnName("from_state").HasMaxLength(100);
        builder.Property(t => t.ToState).HasColumnName("to_state").HasMaxLength(100);
        builder.Property(t => t.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(t => t.TransitionedAt).HasColumnName("transitioned_at");

        builder.Ignore(t => t.DomainEvents);
    }
}
