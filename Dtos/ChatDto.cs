using System;

namespace GarionX.Dtos;

public class ChatDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string PersonalityId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public bool IsShared { get; set; }
    public string? ShareToken { get; set; }
}
