using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using GarionX.Repositories;

namespace GarionX.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsageController : ControllerBase
{
    private readonly IChatRepository _chatRepository;

    // Token budgets per model (monthly/session soft caps for percentage display)
    private static readonly Dictionary<string, long> ModelBudgets = new()
    {
        { "openai", 500_000 },   // 500k tokens for gpt-4o-mini (pay-as-you-go)
        { "gemini", 1_000_000 }, // 1M tokens (Gemini free tier)
        { "claude", 300_000 },   // 300k tokens for claude-3-5-haiku
    };

    public record ModelUsageDto(
        string Model,
        string DisplayName,
        string Color,
        long TokensUsed,
        long Budget,
        double Percentage,
        long TotalRequests,
        DateTime? LastUsed,

        long MonthlyTokensUsed,
        long MonthlyRequests,
        DateTime MonthlyResetTime,

        long WeeklyTokensUsed,
        long WeeklyRequests,
        DateTime WeeklyResetTime,

        long FiveHourlyTokensUsed,
        long FiveHourlyRequests,
        DateTime FiveHourlyResetTime
    );

    public UsageController(IChatRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsage()
    {
        var usages = (await _chatRepository.GetTokenUsagesAsync()).ToList();

        var models = new[] { "openai", "gemini", "claude" };
        var displayNames = new Dictionary<string, string>
        {
            { "openai", "OpenAI GPT-4o-mini" },
            { "gemini", "Google Gemini 2.0 Flash" },
            { "claude", "Anthropic Claude 3.5 Sonnet" },
        };
        var colors = new Dictionary<string, string>
        {
            { "openai", "#10a37f" },
            { "gemini", "#4285f4" },
            { "claude", "#8b5cf6" },
        };

        var result = models.Select(m =>
        {
            var usage = usages.FirstOrDefault(u => u.Model == m);
            long tokensUsed = usage?.TotalTokensUsed ?? 0;
            long budget = ModelBudgets.GetValueOrDefault(m, 500_000);
            double percentage = budget > 0 ? Math.Min(100.0, Math.Round(tokensUsed * 100.0 / budget, 2)) : 0;

            return new ModelUsageDto(
                Model: m,
                DisplayName: displayNames[m],
                Color: colors[m],
                TokensUsed: tokensUsed,
                Budget: budget,
                Percentage: percentage,
                TotalRequests: usage?.TotalRequests ?? 0,
                LastUsed: usage?.UpdatedAt,

                MonthlyTokensUsed: usage?.MonthlyTokensUsed ?? 0,
                MonthlyRequests: usage?.MonthlyRequests ?? 0,
                MonthlyResetTime: usage?.MonthlyResetTime ?? DateTime.UtcNow,

                WeeklyTokensUsed: usage?.WeeklyTokensUsed ?? 0,
                WeeklyRequests: usage?.WeeklyRequests ?? 0,
                WeeklyResetTime: usage?.WeeklyResetTime ?? DateTime.UtcNow,

                FiveHourlyTokensUsed: usage?.FiveHourlyTokensUsed ?? 0,
                FiveHourlyRequests: usage?.FiveHourlyRequests ?? 0,
                FiveHourlyResetTime: usage?.FiveHourlyResetTime ?? DateTime.UtcNow
            );
        }).ToList();

        // Overall stats
        long totalTokens = result.Sum(r => r.TokensUsed);
        long totalRequests = result.Sum(r => r.TotalRequests);

        return Ok(new
        {
            models = result,
            totalTokensUsed = totalTokens,
            totalRequests = totalRequests,
            generatedAt = DateTime.UtcNow
        });
    }
}
