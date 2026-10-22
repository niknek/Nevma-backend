using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Planning.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEventReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderDispatchedAt",
                schema: "planning",
                table: "personal_calendar_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_personal_calendar_events_Status_StartsAt_ReminderDispatched~",
                schema: "planning",
                table: "personal_calendar_events",
                columns: new[] { "Status", "StartsAt", "ReminderDispatchedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_personal_calendar_events_Status_StartsAt_ReminderDispatched~",
                schema: "planning",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "ReminderDispatchedAt",
                schema: "planning",
                table: "personal_calendar_events");
        }
    }
}
