using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WattsOn.Domain.Messaging;

namespace WattsOn.Infrastructure.Persistence.Configurations;

public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.Property(m => m.MessageId).HasColumnName("message_id").HasMaxLength(100).IsRequired();
        builder.Property(m => m.DocumentType).HasColumnName("document_type").HasMaxLength(50).IsRequired();
        builder.Property(m => m.BusinessProcess).HasColumnName("business_process").HasMaxLength(20);
        builder.Property(m => m.SenderGln).HasColumnName("sender_gln").HasMaxLength(13).IsRequired();
        builder.Property(m => m.ReceiverGln).HasColumnName("receiver_gln").HasMaxLength(13).IsRequired();
        builder.Property(m => m.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.ReceivedAt).HasColumnName("received_at");
        builder.Property(m => m.IsProcessed).HasColumnName("is_processed");
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        builder.Property(m => m.ProcessingError).HasColumnName("processing_error").HasMaxLength(2000);
        builder.Property(m => m.ProcessingAttempts).HasColumnName("processing_attempts");
        builder.Property(m => m.ProcessId).HasColumnName("process_id");

        builder.HasIndex(m => m.MessageId).IsUnique();
        builder.HasIndex(m => m.IsProcessed);

        builder.Ignore(m => m.DomainEvents);
    }
}
