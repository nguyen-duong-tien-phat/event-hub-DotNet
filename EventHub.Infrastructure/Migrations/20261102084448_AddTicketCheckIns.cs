using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketCheckIns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BookingId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CheckedInBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketCheckIns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketCheckIns_BookingId_Number",
                table: "TicketCheckIns",
                columns: new[] { "BookingId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketCheckIns");
        }
    }
}
