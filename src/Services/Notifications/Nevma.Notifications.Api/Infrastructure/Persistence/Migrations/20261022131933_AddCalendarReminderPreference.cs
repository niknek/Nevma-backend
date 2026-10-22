using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Notifications.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarReminderPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CalendarRemindersEnabled",
                schema: "notifications",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarRemindersEnabled",
                schema: "notifications",
                table: "notification_preferences");
        }
    }
}
