using System.Collections.Generic;

namespace GarionX.Entities;

public class Personality
{
    public string Id { get; set; } = string.Empty; // e.g. "garionx", "helpful"
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    // Navigation property
    public ICollection<Chat> Chats { get; set; } = new List<Chat>();
}
