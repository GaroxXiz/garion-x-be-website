using System;

namespace GarionX.Dtos;

public class MessageDto
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public string Sender { get; set; } = string.Empty; // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentType { get; set; }
}
