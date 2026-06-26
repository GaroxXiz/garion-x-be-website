using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarionX.Migrations
{
    /// <inheritdoc />
    public partial class AddModelToChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "chats",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Model",
                table: "chats");
        }
    }
}
