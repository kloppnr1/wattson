using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Messaging;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.Property(m => m.DocumentType).HasColumnName("document_type").HasMaxLength(50).IsRequired();
        builder.Property(m => m.BusinessProcess).HasColumnName("business_process").HasMaxLength(20);
        builder.Property(m => m.SenderGln).HasColumnName("sender_gln").HasMaxLength(13).IsRequired();
        builder.Property(m => m.ReceiverGln).HasColumnName("receiver_gln").HasMaxLength(13).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.ProcessId).HasColumnName("process_id");
        builder.Property(m => m.IsSent).HasColumnName("is_sent");
        builder.Property(m => m.SentAt).HasColumnName("sent_at");
        builder.Property(m => m.Response).HasColumnName("response").HasColumnType("jsonb");
        builder.Property(m => m.SendError).HasColumnName("send_error").HasMaxLength(2000);
        builder.Property(m => m.SendAttempts).HasColumnName("send_attempts");
        builder.Property(m => m.ScheduledFor).HasColumnName("scheduled_for");

        builder.HasIndex(m => m.IsSent);

        builder.Ignore(m => m.DomainEvents);
    }
}
