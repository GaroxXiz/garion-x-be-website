using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using GarionX.Entities;
using GarionX.Repositories;
using GarionX.Dtos;
using GarionX.Usecases;

namespace GarionX.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ChatsController : ControllerBase
{
    private readonly IChatRepository _chatRepository;
    private readonly IAiResponseService _aiResponseService;

    public ChatsController(IChatRepository chatRepository, IAiResponseService aiResponseService)
    {
        _chatRepository = chatRepository;
        _aiResponseService = aiResponseService;
    }

    private Guid? GetUserId()
    {
        var userIdString = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value 
                           ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return null;
        }
        return userId;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ChatDto>>> GetChats()
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var chats = await _chatRepository.GetChatsAsync(userId.Value);
        var dtos = chats.Select(c => new ChatDto
        {
            Id = c.Id,
            Title = c.Title,
            CreatedAt = c.CreatedAt,
            PersonalityId = c.PersonalityId,
            Model = c.Model,
            IsPinned = c.IsPinned,
            IsArchived = c.IsArchived,
            IsShared = c.IsShared,
            ShareToken = c.ShareToken
        });

        return Ok(dtos);
    }

    public class CreateChatRequest
    {
        public string PersonalityId { get; set; } = "garionx";
        public string Model { get; set; } = "openai";
    }

    [HttpPost]
    public async Task<ActionResult<ChatDto>> CreateChat([FromBody] CreateChatRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var personality = await _chatRepository.GetPersonalityByIdAsync(request.PersonalityId);
        if (personality == null)
        {
            return BadRequest($"Personality '{request.PersonalityId}' not found.");
        }

        string model = "openai";
        if (request.Model.Equals("claude", StringComparison.OrdinalIgnoreCase)) model = "claude";
        else if (request.Model.Equals("gemini", StringComparison.OrdinalIgnoreCase)) model = "gemini";

        var chat = await _chatRepository.CreateChatAsync(userId.Value, request.PersonalityId, model);
        
        var dto = new ChatDto
        {
            Id = chat.Id,
            Title = chat.Title,
            CreatedAt = chat.CreatedAt,
            PersonalityId = chat.PersonalityId,
            Model = chat.Model,
            IsPinned = chat.IsPinned,
            IsArchived = chat.IsArchived,
            IsShared = chat.IsShared,
            ShareToken = chat.ShareToken
        };

        return CreatedAtAction(nameof(GetChats), new { id = dto.Id }, dto);
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(Guid chatId)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var chat = await _chatRepository.GetChatByIdAsync(chatId);
        if (chat == null || chat.UserId != userId.Value)
        {
            return NotFound();
        }

        await _chatRepository.DeleteChatAsync(chatId);
        return NoContent();
    }

    [HttpGet("{chatId}/messages")]
    public async Task<ActionResult<IEnumerable<MessageDto>>> GetMessages(Guid chatId)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var chat = await _chatRepository.GetChatByIdAsync(chatId);
        if (chat == null || chat.UserId != userId.Value)
        {
            return NotFound();
        }

        var messages = await _chatRepository.GetMessagesAsync(chatId);
        var dtos = messages.Select(m => new MessageDto
        {
            Id = m.Id,
            ChatId = m.ChatId,
            Sender = m.Sender,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            AttachmentUrl = m.AttachmentUrl,
            AttachmentType = m.AttachmentType
        });

        return Ok(dtos);
    }

    public class SendMessageResponse
    {
        public MessageDto UserMessage { get; set; } = null!;
        public MessageDto AssistantMessage { get; set; } = null!;
    }

    [HttpPost("{chatId}/messages")]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(Guid chatId, [FromBody] SendMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrEmpty(request.AttachmentUrl))
        {
            return BadRequest("Content or attachment is required.");
        }

        var chat = await _chatRepository.GetChatByIdAsync(chatId);
        if (chat == null || chat.UserId != userId.Value)
        {
            return NotFound();
        }

        string? attachmentUrl = request.AttachmentUrl;
        string? attachmentType = request.AttachmentType;
        bool hasVideoUrl = false;

        var youtubeRegex = new System.Text.RegularExpressions.Regex(@"(https?://)?(www\.)?(youtube\.com|youtu\.be)/[^\s]+");
        var rawVideoRegex = new System.Text.RegularExpressions.Regex(@"(https?://)[^\s]+\.(mp4|webm|ogg|mkv|mov)(\?[^\s]*)?");

        if (!string.IsNullOrEmpty(attachmentUrl))
        {
            if (youtubeRegex.IsMatch(attachmentUrl) || rawVideoRegex.IsMatch(attachmentUrl))
            {
                hasVideoUrl = true;
                attachmentType = "video";
            }
        }
        else if (!string.IsNullOrEmpty(request.Content))
        {
            var ytMatch = youtubeRegex.Match(request.Content);
            var rawMatch = rawVideoRegex.Match(request.Content);

            if (ytMatch.Success)
            {
                hasVideoUrl = true;
                attachmentUrl = ytMatch.Value;
                attachmentType = "video";
            }
            else if (rawMatch.Success)
            {
                hasVideoUrl = true;
                attachmentUrl = rawMatch.Value;
                attachmentType = "video";
            }
        }

        // 1. Save user message with attachments
        var userMsg = await _chatRepository.AddMessageAsync(chatId, "user", request.Content, attachmentUrl, attachmentType);

        // Update chat model if request contains a valid, different model
        if (!string.IsNullOrWhiteSpace(request.Model) && request.Model != chat.Model)
        {
            string model = "openai";
            if (request.Model.Equals("claude", StringComparison.OrdinalIgnoreCase)) model = "claude";
            else if (request.Model.Equals("gemini", StringComparison.OrdinalIgnoreCase)) model = "gemini";

            chat.Model = model;
            await _chatRepository.UpdateChatModelAsync(chatId, model);
        }

        // 2. If it's the first message or if title is 'New Chat', update the chat title
        var messages = await _chatRepository.GetMessagesAsync(chatId);
        if (chat.Title == "New Chat" || messages.Count() == 1)
        {
            string titleSource = string.IsNullOrWhiteSpace(request.Content) ? "[Attachment]" : request.Content;
            string truncatedTitle = titleSource.Length > 25 
                ? titleSource.Substring(0, 25) + "..." 
                : titleSource;
            await _chatRepository.UpdateChatTitleAsync(chatId, truncatedTitle);
        }

        // 3. Enrich context for AI video summarization
        string aiPromptContent = request.Content;
        if (chat.PersonalityId == "video_summarizer" && attachmentType == "video")
        {
            if (hasVideoUrl)
            {
                string transcriptText = "";
                if (attachmentUrl != null && (attachmentUrl.Contains("youtube.com") || attachmentUrl.Contains("youtu.be")))
                {
                    transcriptText = await GetYoutubeTranscriptAsync(attachmentUrl);
                }

                if (!string.IsNullOrEmpty(transcriptText) && !transcriptText.StartsWith("[Error") && !transcriptText.StartsWith("[No closed"))
                {
                    aiPromptContent += $"\n\n[SYSTEM ANALYSIS PROTOCOL: The user has provided a video link for analysis.\nVideo Link: {attachmentUrl}\n\nVIDEO TRANSCRIPT CONTENT:\n{transcriptText}\n\nYour Task: Analyze the transcript and generate a comprehensive, professional video analysis dossier including an Overview, a visual/audio Timeline of events based on the video transcript timeline, and Key Insights/Takeaways. Maintain a strict cybernetic VidIntel analytical style.]";
                }
                else
                {
                    aiPromptContent += $"\n\n[SYSTEM ANALYSIS PROTOCOL: The user has provided a video link for analysis.\nVideo Link: {attachmentUrl}\n(Note: Subtitle track could not be fetched: {transcriptText})\nYour Task: Generate a comprehensive, professional video analysis dossier based on the video link metadata. Maintain a strict cybernetic VidIntel analytical style.]";
                }
            }
            else
            {
                string fileName = Path.GetFileName(attachmentUrl ?? "");
                aiPromptContent += $"\n\n[SYSTEM ANALYSIS PROTOCOL: The user has uploaded a video file for your immediate analysis.\nFile Name: {fileName}\nYour Task: Generate a comprehensive, professional video analysis dossier including an Overview, a visual/audio Timeline of events based on the video context, and Key Insights/Takeaways. Maintain a strict cybernetic VidIntel analytical style.]";
            }
        }

        // 4. Generate AI response based on chat's model
        var botReplyContent = await _aiResponseService.GetResponseAsync(chatId, aiPromptContent, chat.PersonalityId, chat.Model);

        // 5. Save AI message
        var botMsg = await _chatRepository.AddMessageAsync(chatId, "assistant", botReplyContent);

        var response = new SendMessageResponse
        {
            UserMessage = new MessageDto
            {
                Id = userMsg.Id,
                ChatId = userMsg.ChatId,
                Sender = userMsg.Sender,
                Content = userMsg.Content,
                CreatedAt = userMsg.CreatedAt,
                AttachmentUrl = userMsg.AttachmentUrl,
                AttachmentType = userMsg.AttachmentType
            },
            AssistantMessage = new MessageDto
            {
                Id = botMsg.Id,
                ChatId = botMsg.ChatId,
                Sender = botMsg.Sender,
                Content = botMsg.Content,
                CreatedAt = botMsg.CreatedAt,
                AttachmentUrl = botMsg.AttachmentUrl,
                AttachmentType = botMsg.AttachmentType
            }
        };

        return Ok(response);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(104857600)] // 100MB limit for video files
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file was uploaded.");
        }

        string attachmentType = "image";
        string contentType = file.ContentType.ToLower();
        
        if (contentType.StartsWith("video/"))
        {
            attachmentType = "video";
        }
        else if (!contentType.StartsWith("image/"))
        {
            return BadRequest("Only image and video uploads are supported.");
        }

        try
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var relativePath = $"/uploads/{uniqueFileName}";
            return Ok(new
            {
                url = relativePath,
                type = attachmentType
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPut("{chatId}/pin")]
    public async Task<IActionResult> TogglePin(Guid chatId)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var chat = await _chatRepository.GetChatByIdAsync(chatId);
        if (chat == null || chat.UserId != userId.Value)
        {
            return NotFound();
        }

        await _chatRepository.TogglePinChatAsync(chatId);
        return NoContent();
    }

    [HttpPut("{chatId}/archive")]
    public async Task<IActionResult> ToggleArchive(Guid chatId)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var chat = await _chatRepository.GetChatByIdAsync(chatId);
        if (chat == null || chat.UserId != userId.Value)
        {
            return NotFound();
        }

        await _chatRepository.ToggleArchiveChatAsync(chatId);
        return NoContent();
    }

    [HttpPost("{chatId}/share")]
    public async Task<ActionResult<object>> ShareChat(Guid chatId)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized("Invalid token claims.");
        }

        var chat = await _chatRepository.GetChatByIdAsync(chatId);
        if (chat == null || chat.UserId != userId.Value)
        {
            return NotFound();
        }

        var shareToken = await _chatRepository.ShareChatAsync(chatId);
        return Ok(new { shareToken });
    }

    [AllowAnonymous]
    [HttpGet("shared/{shareToken}")]
    public async Task<ActionResult> GetSharedChat(string shareToken)
    {
        var chat = await _chatRepository.GetSharedChatAsync(shareToken);
        if (chat == null)
        {
            return NotFound("Shared chat not found or has been set to private.");
        }

        var messageDtos = chat.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                ChatId = m.ChatId,
                Sender = m.Sender,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
                AttachmentUrl = m.AttachmentUrl,
                AttachmentType = m.AttachmentType
            });

        return Ok(new
        {
            Title = chat.Title,
            PersonalityName = chat.Personality?.Name ?? "AI",
            Model = chat.Model,
            CreatedAt = chat.CreatedAt,
            Messages = messageDtos
        });
    }

    private string ExtractYoutubeVideoId(string urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId)) return "";
        urlOrId = urlOrId.Trim();
        if (urlOrId.Length == 11 && System.Text.RegularExpressions.Regex.IsMatch(urlOrId, @"^[a-zA-Z0-9_-]{11}$"))
        {
            return urlOrId;
        }

        var match = System.Text.RegularExpressions.Regex.Match(urlOrId, @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/|youtube\.com\/shorts\/)([^""&?\/\s]{11})");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return urlOrId; // fallback
    }

    private async Task<string> GetYoutubeTranscriptAsync(string videoUrlOrId)
    {
        try
        {
            var youtube = new YoutubeExplode.YoutubeClient();
            var videoId = ExtractYoutubeVideoId(videoUrlOrId);
            if (string.IsNullOrEmpty(videoId))
            {
                return "[Error: Could not parse YouTube video ID from link]";
            }
            
            // Get the track manifest
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoId);
            
            // Try to find English, Indonesian, or any track
            var trackInfo = trackManifest.Tracks.FirstOrDefault(t => t.Language.Code == "id")
                            ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Code == "en")
                            ?? trackManifest.Tracks.FirstOrDefault();
                            
            if (trackInfo == null)
            {
                return "[No closed caption track found in the video metadata]";
            }
            
            var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);
            
            var sb = new System.Text.StringBuilder();
            foreach (var caption in track.Captions.Take(1000)) // Limit to 1000 lines to prevent context blowing up
            {
                sb.AppendLine($"[{caption.Offset.ToString(@"hh\:mm\:ss")}] {caption.Text}");
            }
            
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[YoutubeExplode Error] Failed to fetch transcript for {videoUrlOrId}: {ex.Message}");
            return $"[Error retrieving transcript: {ex.Message}]";
        }
    }
}
