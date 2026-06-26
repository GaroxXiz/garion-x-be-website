using System;

namespace GarionX.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChatId { get; set; }
    public string Sender { get; set; } = "user"; // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? AttachmentUrl { get; set; }
    public string? AttachmentType { get; set; }

    // Navigation property
    public Chat? Chat { get; set; }
}
