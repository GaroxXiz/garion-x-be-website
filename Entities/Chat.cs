using System;
using System.Collections.Generic;

namespace GarionX.Entities;

public class Chat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PersonalityId { get; set; } = string.Empty;
    public string Model { get; set; } = "openai";
    
    public bool IsPinned { get; set; } = false;
    public bool IsArchived { get; set; } = false;
    public bool IsShared { get; set; } = false;
    public string? ShareToken { get; set; }
    
    public Guid UserId { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
    public Personality? Personality { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
