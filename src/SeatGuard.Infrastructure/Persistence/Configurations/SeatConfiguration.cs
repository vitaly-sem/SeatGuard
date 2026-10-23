using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Infrastructure.Persistence.Configurations;

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("seats", table =>
        {
            table.HasCheckConstraint("ck_seats_number", "\"number\" > 0");
            table.HasCheckConstraint("ck_seats_status", "\"status\" IN ('Available', 'Held', 'Sold')");
            table.HasCheckConstraint("ck_seats_price_amount", "\"price_amount\" >= 0");
        });

        builder.HasKey(seat => seat.Id);

        builder.Property(seat => seat.Id)
            .HasConversion(id => id.Value, value => new SeatId(value))
            .HasColumnName("id");

        builder.Property(seat => seat.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(seat => seat.Section).HasMaxLength(100).IsRequired();
        builder.Property(seat => seat.Row).HasMaxLength(10).IsRequired();
        builder.Property(seat => seat.Number).IsRequired();
        builder.Property(seat => seat.Label).HasMaxLength(150).IsRequired();
        builder.Property(seat => seat.SoldAt);

        builder.Property(seat => seat.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(seat => seat.HeldByReservationId)
            .HasConversion(new ValueConverter<ReservationId?, Guid?>(
                id => id.HasValue ? id.Value.Value : null,
                value => value.HasValue ? new ReservationId(value.Value) : null))
            .HasColumnName("held_by_reservation_id");

        builder.OwnsOne(seat => seat.Price, price =>
        {
            price.Property(money => money.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(10, 2)
                .IsRequired();

            price.Property(money => money.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(seat => seat.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // Npgsql maps a uint row-version property to the xmin system column.
        // Every update becomes UPDATE ... WHERE id = @id AND xmin = @xmin.
        builder.Property(seat => seat.Version).IsRowVersion();

        builder.HasIndex(seat => new { seat.EventId, seat.Section, seat.Row, seat.Number })
            .IsUnique()
            .HasDatabaseName("ux_seats_position");

        builder.HasIndex(seat => new { seat.EventId, seat.Status })
            .HasDatabaseName("ix_seats_event_id_status");
    }
}
