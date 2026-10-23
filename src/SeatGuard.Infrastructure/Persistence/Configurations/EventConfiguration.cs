using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeatGuard.Domain.Events;

namespace SeatGuard.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events", table =>
        {
            table.HasCheckConstraint("ck_events_seat_count", "\"seat_count\" >= 0");
            table.HasCheckConstraint("ck_events_status", "\"status\" IN ('Published', 'Cancelled')");
        });

        builder.HasKey(@event => @event.Id);

        builder.Property(@event => @event.Id)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("id");

        builder.Property(@event => @event.Name).HasMaxLength(200).IsRequired();
        builder.Property(@event => @event.Venue).HasMaxLength(200).IsRequired();
        builder.Property(@event => @event.StartsAt).IsRequired();
        builder.Property(@event => @event.CreatedAt).IsRequired();
        builder.Property(@event => @event.SeatCount).IsRequired();

        builder.Property(@event => @event.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.OwnsMany(@event => @event.Sections, sections =>
        {
            sections.ToJson("sections");
            sections.Property(section => section.Name).HasMaxLength(100);
            sections.Property(section => section.Rows);
            sections.Property(section => section.SeatsPerRow);
            sections.OwnsOne(section => section.Price, price =>
            {
                price.Property(money => money.Amount).HasPrecision(10, 2);
                price.Property(money => money.Currency).HasMaxLength(3);
            });
        });

        builder.HasIndex(@event => @event.StartsAt).HasDatabaseName("ix_events_starts_at");
    }
}
