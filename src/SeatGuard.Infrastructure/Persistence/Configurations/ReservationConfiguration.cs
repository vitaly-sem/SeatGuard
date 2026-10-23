using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Infrastructure.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations", table =>
        {
            table.HasCheckConstraint(
                "ck_reservations_status",
                "\"status\" IN ('Pending', 'Confirmed', 'Cancelled', 'Expired')");
            table.HasCheckConstraint("ck_reservations_expiry", "\"expires_at\" > \"created_at\"");
            table.HasCheckConstraint("ck_reservations_amount", "\"amount\" >= 0");
        });

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .HasConversion(id => id.Value, value => new ReservationId(value))
            .HasColumnName("id");

        builder.Property(reservation => reservation.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(reservation => reservation.SeatId)
            .HasConversion(id => id.Value, value => new SeatId(value))
            .HasColumnName("seat_id");

        builder.Property(reservation => reservation.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .HasMaxLength(64)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.OwnsOne(reservation => reservation.Amount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("amount")
                .HasPrecision(10, 2)
                .IsRequired();

            amount.Property(money => money.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(reservation => reservation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reservation => reservation.CreatedAt).IsRequired();
        builder.Property(reservation => reservation.ExpiresAt).IsRequired();
        builder.Property(reservation => reservation.ConfirmedAt);
        builder.Property(reservation => reservation.CancelledAt);
        builder.Property(reservation => reservation.ExpiredAt);
        builder.Property(reservation => reservation.TicketCode).HasMaxLength(32);
        builder.Property(reservation => reservation.PaymentReference).HasMaxLength(100);
        builder.Property(reservation => reservation.IdempotencyKey).HasMaxLength(128);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(reservation => reservation.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Seat>()
            .WithMany()
            .HasForeignKey(reservation => reservation.SeatId)
            .OnDelete(DeleteBehavior.Restrict);

        // Last line of defence against overbooking: at most one pending or
        // confirmed reservation may exist for a seat, regardless of code path.
        builder.HasIndex(reservation => reservation.SeatId)
            .IsUnique()
            .HasFilter("\"status\" IN ('Pending', 'Confirmed')")
            .HasDatabaseName("ux_reservations_active_seat");

        builder.HasIndex(reservation => new { reservation.CustomerId, reservation.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"idempotency_key\" IS NOT NULL")
            .HasDatabaseName("ux_reservations_idempotency");

        builder.HasIndex(reservation => new { reservation.Status, reservation.ExpiresAt })
            .HasDatabaseName("ix_reservations_status_expires_at");

        builder.HasIndex(reservation => reservation.TicketCode)
            .IsUnique()
            .HasFilter("\"ticket_code\" IS NOT NULL")
            .HasDatabaseName("ux_reservations_ticket_code");
    }
}
