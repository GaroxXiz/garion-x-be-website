using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarionX.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoSummarizerAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentType",
                table: "messages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.InsertData(
                table: "personalities",
                columns: new[] { "Id", "AvatarUrl", "Description", "Name", "SystemPrompt" },
                values: new object[] { "video_summarizer", "https://api.dicebear.com/7.x/bottts/svg?seed=video_summarizer", "An advanced video analysis companion. Upload a video to generate a structured content summary and timeline.", "VidIntel (Video)", "You are VidIntel, a cybernetic video intelligence analyzer. Your primary function is to summarize and analyze uploaded video files. When a video is uploaded, you must output a structured analysis dossier containing: 1. Video Overview (based on filename and metadata context). 2. Visual & Audio Timeline (a highly detailed breakdown of key events). 3. Key Insights & Summary. 4. Actionable Takeaways. Always maintain a professional, analytical, and highly tech-centric dossier style. If no video has been uploaded yet, politely prompt the user to upload a video for analysis." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "personalities",
                keyColumn: "Id",
                keyValue: "video_summarizer");

            migrationBuilder.DropColumn(
                name: "AttachmentType",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "messages");
        }
    }
}
