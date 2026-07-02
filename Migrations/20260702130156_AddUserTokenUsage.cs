using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GarionX.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTokenUsage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("TRUNCATE TABLE token_usages CASCADE;");

            migrationBuilder.DropIndex(
                name: "IX_token_usages_Model",
                table: "token_usages");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "token_usages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));



            migrationBuilder.CreateIndex(
                name: "IX_token_usages_Model_UserId",
                table: "token_usages",
                columns: new[] { "Model", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_token_usages_UserId",
                table: "token_usages",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_token_usages_users_UserId",
                table: "token_usages",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_token_usages_users_UserId",
                table: "token_usages");

            migrationBuilder.DropIndex(
                name: "IX_token_usages_Model_UserId",
                table: "token_usages");

            migrationBuilder.DropIndex(
                name: "IX_token_usages_UserId",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "token_usages");

            migrationBuilder.CreateIndex(
                name: "IX_token_usages_Model",
                table: "token_usages",
                column: "Model",
                unique: true);
        }
    }
}
