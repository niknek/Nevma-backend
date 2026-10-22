using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Planning.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantResponseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "DeclinedParticipantIds",
                schema: "planning",
                table: "personal_calendar_events",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "DeclinedParticipantReasons",
                schema: "planning",
                table: "personal_calendar_events",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclinedParticipantIds",
                schema: "planning",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "DeclinedParticipantReasons",
                schema: "planning",
                table: "personal_calendar_events");
        }
    }
}
