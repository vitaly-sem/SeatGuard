using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeatGuard.Infrastructure.Outbox;

namespace SeatGuard.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type).HasMaxLength(500).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.OccurredAt).IsRequired();
        builder.Property(message => message.ProcessedAt);
        builder.Property(message => message.FailedAt);
        builder.Property(message => message.Attempts).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2000);
        builder.Property(message => message.NextAttemptAt).IsRequired();

        builder.HasIndex(message => new { message.ProcessedAt, message.FailedAt, message.NextAttemptAt })
            .HasDatabaseName("ix_outbox_pending")
            .HasFilter("\"processed_at\" IS NULL AND \"failed_at\" IS NULL");

        // Retention scans only processed rows, ordered by processing time. A
        // partial index keeps that query off the growing table.
        builder.HasIndex(message => message.ProcessedAt)
            .HasDatabaseName("ix_outbox_processed_at")
            .HasFilter("\"processed_at\" IS NOT NULL");
    }
}
