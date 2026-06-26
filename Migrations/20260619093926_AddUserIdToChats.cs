using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarionX.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM messages; DELETE FROM chats;");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "chats",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_chats_UserId",
                table: "chats",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_chats_users_UserId",
                table: "chats",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chats_users_UserId",
                table: "chats");

            migrationBuilder.DropIndex(
                name: "IX_chats_UserId",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "chats");
        }
    }
}
