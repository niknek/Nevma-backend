using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nevma.Identity.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressBookContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "address_book_contacts",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Social = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address_book_contacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_address_book_contacts_LinkedUserId",
                schema: "identity",
                table: "address_book_contacts",
                column: "LinkedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_address_book_contacts_OwnerId_Name",
                schema: "identity",
                table: "address_book_contacts",
                columns: new[] { "OwnerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_address_book_contacts_OwnerId_Phone",
                schema: "identity",
                table: "address_book_contacts",
                columns: new[] { "OwnerId", "Phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_book_contacts",
                schema: "identity");
        }
    }
}
