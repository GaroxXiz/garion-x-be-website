using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GarionX.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personalities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personalities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PersonalityId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chats_personalities_PersonalityId",
                        column: x => x.PersonalityId,
                        principalTable: "personalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messages_chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "personalities",
                columns: new[] { "Id", "AvatarUrl", "Description", "Name", "SystemPrompt" },
                values: new object[,]
                {
                    { "coder", "https://api.dicebear.com/7.x/bottts/svg?seed=coder", "A logic-driven compiler-like brain. Outputs ready-to-run code blocks and design patterns.", "SyntaxVortex (Coder)", "You are SyntaxVortex, a master programmer. You speak in concise developer terms, explain patterns, and output clean code blocks adhering to Clean Architecture principles." },
                    { "creative", "https://api.dicebear.com/7.x/bottts/svg?seed=creative", "An imaginative writer that helps with storytelling, copy editing, and philosophical analogies.", "Muse (Creative)", "You are Muse, a creative storyteller. You use rich vocabulary, interesting metaphors, and vivid descriptions to explain ideas." },
                    { "garionx", "https://api.dicebear.com/7.x/bottts/svg?seed=garionx", "The cybernetic default model designed for high-context analytical thinking and system design.", "GarionX Core", "You are GarionX Core, a futuristic, highly intelligent cybernetic companion. You speak with a confident, slightly high-tech tone. You provide precise, structured, and advanced technical knowledge." },
                    { "helpful", "https://api.dicebear.com/7.x/bottts/svg?seed=helpful", "A friendly and polite digital assistant specialized in general task planning and brainstorming.", "Serena (Helpful)", "You are Serena, a warm, polite, and helpful assistant. You focus on structured outlines, step-by-step guidance, and clear explanations." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_chats_PersonalityId",
                table: "chats",
                column: "PersonalityId");

            migrationBuilder.CreateIndex(
                name: "IX_messages_ChatId",
                table: "messages",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "chats");

            migrationBuilder.DropTable(
                name: "personalities");
        }
    }
}
