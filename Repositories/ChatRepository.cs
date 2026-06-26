using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GarionX.Entities;

namespace GarionX.Repositories;

public interface IChatRepository
{
    Task<IEnumerable<Chat>> GetChatsAsync(Guid userId);
    Task<Chat?> GetChatByIdAsync(Guid chatId);
    Task<Chat> CreateChatAsync(Guid userId, string personalityId, string model);
    Task DeleteChatAsync(Guid chatId);
    Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId);
    Task<Message> AddMessageAsync(Guid chatId, string sender, string content, string? attachmentUrl = null, string? attachmentType = null);
    Task UpdateChatTitleAsync(Guid chatId, string title);
    Task UpdateChatModelAsync(Guid chatId, string model);
    Task<IEnumerable<Personality>> GetPersonalitiesAsync();
    Task<Personality?> GetPersonalityByIdAsync(string id);
    Task<Personality> CreatePersonalityAsync(Personality personality);
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> GetUserByIdAsync(Guid userId);
    Task<User> RegisterUserAsync(User user);
    Task UpdateUserPasswordAsync(string username, string newPasswordHash);
    Task UpdateUserProfileAsync(Guid userId, string name, string email, string? avatarUrl);
    Task<IEnumerable<TokenUsage>> GetTokenUsagesAsync();
    Task IncrementTokenUsageAsync(string model, long tokensUsed);
    Task TogglePinChatAsync(Guid chatId);
    Task ToggleArchiveChatAsync(Guid chatId);
    Task<string> ShareChatAsync(Guid chatId);
    Task<Chat?> GetSharedChatAsync(string shareToken);
}

public class ChatRepository : IChatRepository
{
    private readonly GarionXDbContext _dbContext;

    public ChatRepository(GarionXDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<Chat>> GetChatsAsync(Guid userId)
    {
        return await _dbContext.Chats
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Chat?> GetChatByIdAsync(Guid chatId)
    {
        return await _dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == chatId);
    }

    public async Task<Chat> CreateChatAsync(Guid userId, string personalityId, string model)
    {
        var chat = new Chat
        {
            UserId = userId,
            PersonalityId = personalityId,
            Model = model
        };
        _dbContext.Chats.Add(chat);
        await _dbContext.SaveChangesAsync();
        return chat;
    }

    public async Task DeleteChatAsync(Guid chatId)
    {
        var chat = await _dbContext.Chats.FindAsync(chatId);
        if (chat != null)
        {
            _dbContext.Chats.Remove(chat);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId)
    {
        return await _dbContext.Messages
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<Message> AddMessageAsync(Guid chatId, string sender, string content, string? attachmentUrl = null, string? attachmentType = null)
    {
        var message = new Message
        {
            ChatId = chatId,
            Sender = sender,
            Content = content,
            AttachmentUrl = attachmentUrl,
            AttachmentType = attachmentType
        };
        _dbContext.Messages.Add(message);
        await _dbContext.SaveChangesAsync();
        return message;
    }

    public async Task UpdateChatTitleAsync(Guid chatId, string title)
    {
        var chat = await _dbContext.Chats.FindAsync(chatId);
        if (chat != null)
        {
            chat.Title = title;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateChatModelAsync(Guid chatId, string model)
    {
        var chat = await _dbContext.Chats.FindAsync(chatId);
        if (chat != null)
        {
            chat.Model = model;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<Personality>> GetPersonalitiesAsync()
    {
        return await _dbContext.Personalities.ToListAsync();
    }

    public async Task<Personality?> GetPersonalityByIdAsync(string id)
    {
        return await _dbContext.Personalities.FindAsync(id);
    }
    
    public async Task<Personality> CreatePersonalityAsync(Personality personality)
    {
        _dbContext.Personalities.Add(personality);
        await _dbContext.SaveChangesAsync();
        return personality;
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() || u.Email.ToLower() == username.ToLower());
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _dbContext.Users.FindAsync(userId);
    }

    public async Task<User> RegisterUserAsync(User user)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    public async Task UpdateUserPasswordAsync(string username, string newPasswordHash)
    {
        var user = await GetUserByUsernameAsync(username);
        if (user != null)
        {
            user.PasswordHash = newPasswordHash;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateUserProfileAsync(Guid userId, string name, string email, string? avatarUrl)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user != null)
        {
            user.Name = name;
            user.Email = email;
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                user.AvatarUrl = avatarUrl;
            }
            await _dbContext.SaveChangesAsync();
        }
    }

    private static DateTime GetNextMonthlyReset(DateTime now)
    {
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
    }

    private static DateTime GetNextWeeklyReset(DateTime now)
    {
        int daysToMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysToMonday == 0) daysToMonday = 7;
        return now.Date.AddDays(daysToMonday);
    }

    private static DateTime GetNextFiveHourlyReset(DateTime now)
    {
        var hourBlock = (now.Hour / 5) * 5;
        var blockStart = new DateTime(now.Year, now.Month, now.Day, hourBlock, 0, 0, DateTimeKind.Utc);
        return blockStart.AddHours(5);
    }

    private static bool CheckAndApplyResets(TokenUsage usage, DateTime now)
    {
        bool changed = false;

        if (now >= usage.MonthlyResetTime)
        {
            usage.MonthlyTokensUsed = 0;
            usage.MonthlyRequests = 0;
            usage.MonthlyResetTime = GetNextMonthlyReset(now);
            changed = true;
        }

        if (now >= usage.WeeklyResetTime)
        {
            usage.WeeklyTokensUsed = 0;
            usage.WeeklyRequests = 0;
            usage.WeeklyResetTime = GetNextWeeklyReset(now);
            changed = true;
        }

        if (now >= usage.FiveHourlyResetTime)
        {
            usage.FiveHourlyTokensUsed = 0;
            usage.FiveHourlyRequests = 0;
            usage.FiveHourlyResetTime = GetNextFiveHourlyReset(now);
            changed = true;
        }

        return changed;
    }

    public async Task<IEnumerable<TokenUsage>> GetTokenUsagesAsync()
    {
        var now = DateTime.UtcNow;
        var usages = await _dbContext.TokenUsages.ToListAsync();
        bool changed = false;
        foreach (var usage in usages)
        {
            if (CheckAndApplyResets(usage, now))
            {
                changed = true;
            }
        }
        if (changed)
        {
            await _dbContext.SaveChangesAsync();
        }

        return usages.OrderBy(t => t.Model);
    }

    public async Task IncrementTokenUsageAsync(string model, long tokensUsed)
    {
        var now = DateTime.UtcNow;
        var existing = await _dbContext.TokenUsages
            .FirstOrDefaultAsync(t => t.Model == model);

        if (existing == null)
        {
            var nextMonthly = GetNextMonthlyReset(now);
            var nextWeekly = GetNextWeeklyReset(now);
            var nextFiveHourly = GetNextFiveHourlyReset(now);

            _dbContext.TokenUsages.Add(new TokenUsage
            {
                Model = model,
                TotalTokensUsed = tokensUsed,
                TotalRequests = 1,
                CreatedAt = now,
                UpdatedAt = now,

                MonthlyTokensUsed = tokensUsed,
                MonthlyRequests = 1,
                MonthlyResetTime = nextMonthly,

                WeeklyTokensUsed = tokensUsed,
                WeeklyRequests = 1,
                WeeklyResetTime = nextWeekly,

                FiveHourlyTokensUsed = tokensUsed,
                FiveHourlyRequests = 1,
                FiveHourlyResetTime = nextFiveHourly
            });
        }
        else
        {
            CheckAndApplyResets(existing, now);

            existing.TotalTokensUsed += tokensUsed;
            existing.TotalRequests += 1;

            existing.MonthlyTokensUsed += tokensUsed;
            existing.MonthlyRequests += 1;

            existing.WeeklyTokensUsed += tokensUsed;
            existing.WeeklyRequests += 1;

            existing.FiveHourlyTokensUsed += tokensUsed;
            existing.FiveHourlyRequests += 1;

            existing.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task TogglePinChatAsync(Guid chatId)
    {
        var chat = await _dbContext.Chats.FindAsync(chatId);
        if (chat != null)
        {
            chat.IsPinned = !chat.IsPinned;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task ToggleArchiveChatAsync(Guid chatId)
    {
        var chat = await _dbContext.Chats.FindAsync(chatId);
        if (chat != null)
        {
            chat.IsArchived = !chat.IsArchived;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<string> ShareChatAsync(Guid chatId)
    {
        var chat = await _dbContext.Chats.FindAsync(chatId);
        if (chat != null)
        {
            if (string.IsNullOrEmpty(chat.ShareToken))
            {
                chat.ShareToken = Guid.NewGuid().ToString("N");
            }
            chat.IsShared = true;
            await _dbContext.SaveChangesAsync();
            return chat.ShareToken;
        }
        return string.Empty;
    }

    public async Task<Chat?> GetSharedChatAsync(string shareToken)
    {
        return await _dbContext.Chats
            .Include(c => c.Messages)
            .Include(c => c.Personality)
            .FirstOrDefaultAsync(c => c.ShareToken == shareToken && c.IsShared);
    }
}
