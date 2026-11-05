using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAndBookingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Emails are stored lower case from now on. Bring existing rows in line first; if two of them
            // differ only by case, building the unique index below fails and nothing is applied.
            migrationBuilder.Sql("UPDATE \"Users\" SET \"Email\" = lower(trim(\"Email\"));");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartsAt",
                table: "Events",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_EventId_CreatedAt",
                table: "Bookings",
                columns: new[] { "EventId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PaymentIntentId",
                table: "Bookings",
                column: "PaymentIntentId",
                unique: true,
                filter: "\"PaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Pending_CreatedAt",
                table: "Bookings",
                column: "CreatedAt",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId_CreatedAt",
                table: "Bookings",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Events_StartsAt",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_EventId_CreatedAt",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_PaymentIntentId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Pending_CreatedAt",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_UserId_CreatedAt",
                table: "Bookings");
        }
    }
}
