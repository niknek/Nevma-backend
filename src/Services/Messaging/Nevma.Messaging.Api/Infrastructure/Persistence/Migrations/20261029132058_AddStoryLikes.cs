using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Messaging.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "LikedByIds",
                schema: "messaging",
                table: "stories",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LikedByIds",
                schema: "messaging",
                table: "stories");
        }
    }
}
