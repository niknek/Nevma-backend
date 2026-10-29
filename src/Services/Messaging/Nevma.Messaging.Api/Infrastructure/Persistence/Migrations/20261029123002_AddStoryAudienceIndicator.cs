using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Messaging.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryAudienceIndicator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRestrictedAudience",
                schema: "messaging",
                table: "stories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRestrictedAudience",
                schema: "messaging",
                table: "stories");
        }
    }
}
