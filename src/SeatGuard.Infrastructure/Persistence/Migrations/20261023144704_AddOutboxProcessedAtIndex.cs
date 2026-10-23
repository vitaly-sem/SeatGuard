using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeatGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxProcessedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outbox_processed_at",
                table: "outbox_messages",
                column: "processed_at",
                filter: "\"processed_at\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_processed_at",
                table: "outbox_messages");
        }
    }
}
