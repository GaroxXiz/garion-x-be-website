using System;

namespace GarionX.Entities;

public class TokenUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Model { get; set; } = string.Empty;       // "openai" | "gemini" | "claude"
    public long TotalTokensUsed { get; set; } = 0;
    public long TotalRequests { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public long MonthlyTokensUsed { get; set; } = 0;
    public long MonthlyRequests { get; set; } = 0;
    public DateTime MonthlyResetTime { get; set; } = DateTime.UtcNow;

    public long WeeklyTokensUsed { get; set; } = 0;
    public long WeeklyRequests { get; set; } = 0;
    public DateTime WeeklyResetTime { get; set; } = DateTime.UtcNow;

    public long FiveHourlyTokensUsed { get; set; } = 0;
    public long FiveHourlyRequests { get; set; } = 0;
    public DateTime FiveHourlyResetTime { get; set; } = DateTime.UtcNow;
}
