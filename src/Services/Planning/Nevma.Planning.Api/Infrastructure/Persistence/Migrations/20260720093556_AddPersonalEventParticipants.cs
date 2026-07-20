using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Planning.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalEventParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "AcceptedParticipantIds",
                schema: "planning",
                table: "personal_calendar_events",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "ParticipantIds",
                schema: "planning",
                table: "personal_calendar_events",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "planning",
                table: "personal_calendar_events",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_personal_calendar_events_ParticipantIds",
                schema: "planning",
                table: "personal_calendar_events",
                column: "ParticipantIds")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_personal_calendar_events_ParticipantIds",
                schema: "planning",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "AcceptedParticipantIds",
                schema: "planning",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "ParticipantIds",
                schema: "planning",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "planning",
                table: "personal_calendar_events");
        }
    }
}
