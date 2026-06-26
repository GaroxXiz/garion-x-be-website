using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarionX.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenIntervalsAndImagePersonality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FiveHourlyRequests",
                table: "token_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiveHourlyResetTime",
                table: "token_usages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "FiveHourlyTokensUsed",
                table: "token_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyRequests",
                table: "token_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "MonthlyResetTime",
                table: "token_usages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "MonthlyTokensUsed",
                table: "token_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "WeeklyRequests",
                table: "token_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "WeeklyResetTime",
                table: "token_usages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "WeeklyTokensUsed",
                table: "token_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.InsertData(
                table: "personalities",
                columns: new[] { "Id", "AvatarUrl", "Description", "Name", "SystemPrompt" },
                values: new object[] { "image_generator", "https://api.dicebear.com/7.x/bottts/svg?seed=image_generator", "A creative cybernetic illustrator. Converts text prompts into gorgeous visual artwork.", "Synthetix (Image)", "You are Synthetix, a cybernetic image generator. Your only function is to generate images based on user prompts. For every request, you MUST: 1. Translate it to English if necessary and enrich it with beautiful cyberpunk details. 2. Generate a markdown image referencing: https://image.pollinations.ai/prompt/{url_encoded_prompt}?nologo=true. The prompt inside the URL must be fully URL-encoded (spaces replaced with %20). 3. Output the markdown image directly, followed by a brief, sleek description of the artwork. Example format: ![Artwork](https://image.pollinations.ai/prompt/futuristic%20cyborg?nologo=true). Never output code blocks, HTML, or code wrappers around the image syntax." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "personalities",
                keyColumn: "Id",
                keyValue: "image_generator");

            migrationBuilder.DropColumn(
                name: "FiveHourlyRequests",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "FiveHourlyResetTime",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "FiveHourlyTokensUsed",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "MonthlyRequests",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "MonthlyResetTime",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "MonthlyTokensUsed",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "WeeklyRequests",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "WeeklyResetTime",
                table: "token_usages");

            migrationBuilder.DropColumn(
                name: "WeeklyTokensUsed",
                table: "token_usages");
        }
    }
}
