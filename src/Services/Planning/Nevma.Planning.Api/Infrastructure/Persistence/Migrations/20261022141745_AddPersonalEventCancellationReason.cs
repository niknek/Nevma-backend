using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Planning.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalEventCancellationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "planning",
                table: "personal_calendar_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "planning",
                table: "personal_calendar_events");
        }
    }
}
