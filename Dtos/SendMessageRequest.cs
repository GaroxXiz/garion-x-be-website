namespace GarionX.Dtos;

public class SendMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public string? AttachmentType { get; set; }
}
