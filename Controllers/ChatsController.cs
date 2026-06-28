using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

        // Determine active personality (handle auto routing)
        string activePersonalityId = chat.PersonalityId;
        bool isAutoRouted = false;
        if (chat.PersonalityId == "auto")
        {
            activePersonalityId = await ClassifyPersonalityAsync(request.Content);
            isAutoRouted = true;
        }

        // 3. Enrich context for AI video summarization
        string aiPromptContent = request.Content;
        if (activePersonalityId == "video_summarizer" && attachmentType == "video")
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
        var botReplyContent = await _aiResponseService.GetResponseAsync(chatId, aiPromptContent, activePersonalityId, chat.Model);

        if (isAutoRouted)
        {
            string badge = activePersonalityId switch
            {
                "coder" => "🤖 **[Auto-Routing: SyntaxVortex (Coder)]**\n\n",
                "image_generator" => "🤖 **[Auto-Routing: Synthetix (Image)]**\n\n",
                "video_generator" => "🤖 **[Auto-Routing: AnimateX (Video)]**\n\n",
                "video_summarizer" => "🤖 **[Auto-Routing: VidIntel (Video Analyst)]**\n\n",
                "creative" => "🤖 **[Auto-Routing: Muse (Creative)]**\n\n",
                "helpful" => "🤖 **[Auto-Routing: Serena (Helpful)]**\n\n",
                _ => "🤖 **[Auto-Routing: GarionX Core]**\n\n"
            };
            botReplyContent = badge + botReplyContent;
        }

        // Determine if we need to attach the mock animated video or call real Fal.ai API
        // Determine if we need to attach the mock animated video or call real API
        string? replyAttachmentUrl = null;
        string? replyAttachmentType = null;
        if (activePersonalityId == "video_generator" && userMsg.AttachmentType == "image" && !string.IsNullOrEmpty(userMsg.AttachmentUrl))
        {
            // Default to local fallback simulation
            replyAttachmentUrl = userMsg.AttachmentUrl;
            replyAttachmentType = "video";

            var segmindApiKey = Environment.GetEnvironmentVariable("SEGMIND_API_KEY");
            var replicateToken = Environment.GetEnvironmentVariable("REPLICATE_API_KEY");
            string? apiError = null;

            if (!string.IsNullOrEmpty(segmindApiKey))
            {
                // Call Segmind SVD API (100 free daily credits!)
                try
                {
                    // Construct public image URL.
                    var absoluteImageUrl = $"{Request.Scheme}://{Request.Host}{userMsg.AttachmentUrl}";
                    string imageToAnimate = absoluteImageUrl;
                    
                    // Localhost/Internal containers cannot be accessed by public APIs.
                    if (imageToAnimate.Contains("localhost") || imageToAnimate.Contains("127.0.0.1") || imageToAnimate.Contains("::1"))
                    {
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        var fileName = Path.GetFileName(userMsg.AttachmentUrl);
                        var localFilePath = Path.Combine(uploadsFolder, fileName);
                        
                        if (System.IO.File.Exists(localFilePath))
                        {
                            var publicUrl = await UploadToPublicHostAsync(localFilePath);
                            if (!string.IsNullOrEmpty(publicUrl))
                            {
                                imageToAnimate = publicUrl;
                            }
                        }
                    }

                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(90);
                    client.DefaultRequestHeaders.Add("x-api-key", segmindApiKey);

                    var requestBody = new { 
                        image = imageToAnimate,
                        base64 = false
                    };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                    var apiResponse = await client.PostAsync("https://api.segmind.com/v1/svd", jsonContent);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        var contentType = apiResponse.Content.Headers.ContentType?.MediaType ?? "";
                        if (contentType.Contains("json"))
                        {
                            var responseJson = await apiResponse.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(responseJson);
                            if (doc.RootElement.TryGetProperty("output", out var outputProp))
                            {
                                var outputVal = outputProp.GetString();
                                if (!string.IsNullOrEmpty(outputVal))
                                {
                                    replyAttachmentUrl = outputVal;
                                }
                            }
                        }
                        else
                        {
                            // It returned the raw video bytes directly
                            var videoBytes = await apiResponse.Content.ReadAsByteArrayAsync();
                            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                            var uniqueVideoName = $"{Guid.NewGuid()}.mp4";
                            var videoPath = Path.Combine(uploadsFolder, uniqueVideoName);
                            
                            await System.IO.File.WriteAllBytesAsync(videoPath, videoBytes);
                            replyAttachmentUrl = $"/uploads/{uniqueVideoName}";
                        }
                    }
                    else
                    {
                        var err = await apiResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[Segmind Video Gen Error]: {err}");
                        apiError = $"Segmind Status {apiResponse.StatusCode}: {err}";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Segmind Connection Exception]: {ex.Message}");
                    apiError = $"Segmind Exception: {ex.Message}";
                }
            }
            else if (!string.IsNullOrEmpty(replicateToken))
            {
                // Call Replicate SVD API
                try
                {
                    // Construct public image URL.
                    var absoluteImageUrl = $"{Request.Scheme}://{Request.Host}{userMsg.AttachmentUrl}";
                    string imageToAnimate = absoluteImageUrl;
                    
                    if (imageToAnimate.Contains("localhost") || imageToAnimate.Contains("127.0.0.1") || imageToAnimate.Contains("::1"))
                    {
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        var fileName = Path.GetFileName(userMsg.AttachmentUrl);
                        var localFilePath = Path.Combine(uploadsFolder, fileName);
                        
                        if (System.IO.File.Exists(localFilePath))
                        {
                            var publicUrl = await UploadToPublicHostAsync(localFilePath);
                            if (!string.IsNullOrEmpty(publicUrl))
                            {
                                imageToAnimate = publicUrl;
                            }
                            else
                            {
                                imageToAnimate = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?q=80&w=1000";
                            }
                        }
                        else
                        {
                            imageToAnimate = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?q=80&w=1000";
                        }
                    }

                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(120); // Replicate generation can take up to 2 mins
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", replicateToken);

                    var requestBody = new { 
                        version = "3f0457e4619daac51203dedb472816fd4af51f3149fa7a9e0b5ffcf1b8172438",
                        input = new {
                            input_image = imageToAnimate
                        }
                    };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                    var apiResponse = await client.PostAsync("https://api.replicate.com/v1/predictions", jsonContent);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        var responseJson = await apiResponse.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(responseJson);
                        
                        if (doc.RootElement.TryGetProperty("urls", out var urlsProp) && 
                            urlsProp.TryGetProperty("get", out var getUrlProp))
                        {
                            var getUrl = getUrlProp.GetString();
                            if (!string.IsNullOrEmpty(getUrl))
                            {
                                // Poll the prediction status
                                string status = "starting";
                                string? videoUrl = null;
                                int maxAttempts = 35; // 35 attempts * 2 seconds = 70 seconds max
                                
                                for (int attempt = 0; attempt < maxAttempts; attempt++)
                                {
                                    await Task.Delay(2000); // wait 2 seconds between checks
                                    
                                    var checkResponse = await client.GetAsync(getUrl);
                                    if (checkResponse.IsSuccessStatusCode)
                                    {
                                        var checkJson = await checkResponse.Content.ReadAsStringAsync();
                                        using var checkDoc = JsonDocument.Parse(checkJson);
                                        
                                        if (checkDoc.RootElement.TryGetProperty("status", out var statusProp))
                                        {
                                            status = statusProp.GetString() ?? "failed";
                                            if (status == "succeeded")
                                            {
                                                if (checkDoc.RootElement.TryGetProperty("output", out var outputProp))
                                                {
                                                    if (outputProp.ValueKind == JsonValueKind.Array && outputProp.GetArrayLength() > 0)
                                                    {
                                                        videoUrl = outputProp[0].GetString();
                                                    }
                                                    else if (outputProp.ValueKind == JsonValueKind.String)
                                                    {
                                                        videoUrl = outputProp.GetString();
                                                    }
                                                }
                                                break;
                                            }
                                            else if (status == "failed" || status == "canceled")
                                            {
                                                apiError = $"Replicate prediction status ended with: {status}";
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        apiError = $"Replicate status check failed with: {checkResponse.StatusCode}";
                                        break;
                                    }
                                }

                                if (!string.IsNullOrEmpty(videoUrl))
                                {
                                    replyAttachmentUrl = videoUrl;
                                }
                                else if (string.IsNullOrEmpty(apiError))
                                {
                                    apiError = $"Replicate prediction timed out (final status: {status})";
                                }
                            }
                        }
                    }
                    else
                    {
                        var err = await apiResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[Replicate Video Gen Error]: {err}");
                        apiError = $"Status {apiResponse.StatusCode}: {err}";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Replicate Connection Exception]: {ex.Message}");
                    apiError = $"Connection Exception: {ex.Message}";
                }
            }
            else
            {
                apiError = "Environment variable SEGMIND_API_KEY or REPLICATE_API_KEY is not configured on the server.";
            }

            // Append diagnostics to LLM reply content for visibility
            if (!string.IsNullOrEmpty(apiError))
            {
                botReplyContent += $"\n\n---\n⚠️ **[Video Generator Diagnostics]** Mode Simulasi diaktifkan karena:\n`{apiError}`\n\n*Hubungi administrator untuk memasang SEGMIND_API_KEY (gratis 100x sehari) atau REPLICATE_API_KEY.*";
            }
        }

        // 5. Save AI message
        var botMsg = await _chatRepository.AddMessageAsync(chatId, "assistant", botReplyContent, replyAttachmentUrl, replyAttachmentType);

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

    private async Task<string?> UploadToPublicHostAsync(string localPath)
    {
        try
        {
            using var client = new HttpClient();
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);
            
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(streamContent, "file", Path.GetFileName(localPath));

            var response = await client.PostAsync("https://tmpfiles.org/api/v1/upload", form);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) && 
                    dataProp.TryGetProperty("url", out var urlProp))
                {
                    var tmpUrl = urlProp.GetString();
                    if (!string.IsNullOrEmpty(tmpUrl))
                    {
                        return tmpUrl.Replace("https://tmpfiles.org/", "https://tmpfiles.org/dl/");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Public Host Upload Exception]: {ex.Message}");
        }
        return null;
    }

    private async Task<string> ClassifyPersonalityAsync(string userPrompt)
    {
        // Default fallback is the default bot
        string defaultPersonality = "garionx";
        
        // Quick local regex classifications to avoid API calls for obvious keywords (highly optimized & instant!)
        string lowerPrompt = userPrompt.ToLower();
        if (lowerPrompt.Contains("code") || lowerPrompt.Contains("python") || lowerPrompt.Contains("javascript") || 
            lowerPrompt.Contains("css") || lowerPrompt.Contains("html") || lowerPrompt.Contains("c#") || 
            lowerPrompt.Contains("java") || lowerPrompt.Contains("program") || lowerPrompt.Contains("syntax") ||
            lowerPrompt.Contains("function") || lowerPrompt.Contains("bug") || lowerPrompt.Contains("debug") ||
            lowerPrompt.Contains("error in line") || lowerPrompt.Contains("database") || lowerPrompt.Contains("query"))
        {
            return "coder"; // SyntaxVortex
        }
        if (lowerPrompt.Contains("gambar") || lowerPrompt.Contains("lukisan") || lowerPrompt.Contains("draw") || 
            lowerPrompt.Contains("paint") || lowerPrompt.Contains("ilustrasi") || lowerPrompt.Contains("foto") ||
            lowerPrompt.Contains("generate image") || lowerPrompt.Contains("synthetix") || lowerPrompt.Contains("wallpaper"))
        {
            return "image_generator"; // Synthetix
        }
        if (lowerPrompt.Contains("video") || lowerPrompt.Contains("animasi") || lowerPrompt.Contains("animate") || 
            lowerPrompt.Contains("buat video") || lowerPrompt.Contains("gif") || lowerPrompt.Contains("motion"))
        {
            // If it's about analyzing/summarizing video content, route to video_summarizer. 
            // Otherwise, if they want to make/generate/animate a video, route to video_generator.
            if (lowerPrompt.Contains("summarize") || lowerPrompt.Contains("ringkas") || lowerPrompt.Contains("analisis") || 
                lowerPrompt.Contains("analize") || lowerPrompt.Contains("isi video"))
            {
                return "video_summarizer"; // VidIntel
            }
            return "video_generator"; // AnimateX
        }
        if (lowerPrompt.Contains("puisi") || lowerPrompt.Contains("cerita") || lowerPrompt.Contains("novel") || 
            lowerPrompt.Contains("story") || lowerPrompt.Contains("dongeng") || lowerPrompt.Contains("pantun") ||
            lowerPrompt.Contains("menulis") || lowerPrompt.Contains("creative") || lowerPrompt.Contains("analogi"))
        {
            return "creative"; // Muse
        }
        if (lowerPrompt.Contains("bantu") || lowerPrompt.Contains("help") || lowerPrompt.Contains("plan") || 
            lowerPrompt.Contains("rencana") || lowerPrompt.Contains("jadwal") || lowerPrompt.Contains("tips") ||
            lowerPrompt.Contains("saran") || lowerPrompt.Contains("brainstorm"))
        {
            return "helpful"; // Serena
        }

        // If no keyword matches, use a quick LLM classification call!
        var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (!string.IsNullOrEmpty(groqApiKey) && groqApiKey != "your_groq_api_key_here")
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5); // super short timeout for fast routing
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);

                var messages = new[]
                {
                    new { role = "system", content = @"You are a prompt router. Classify the user prompt and respond with EXACTLY one of these personality IDs:
- coder (for programming, algorithms, code blocks, or debug issues)
- image_generator (for drawing, painting, or generating images)
- video_generator (for creating/animating videos from pictures)
- video_summarizer (for summarizing/analyzing video files)
- creative (for poetry, stories, creative writing, copy editing)
- helpful (for step-by-step task planning, brainstorming, friendly assistant)
- garionx (for general chat, greetings, or fallback)

Respond with ONLY the lowercase string ID from the list above, with no markdown, no punctuation, and no explanation. Example: coder" },
                    new { role = "user", content = userPrompt }
                };

                var requestBody = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = messages,
                    temperature = 0.0,
                    max_tokens = 10
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var classification = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim().ToLower();
                        var validIds = new[] { "coder", "image_generator", "video_generator", "video_summarizer", "creative", "helpful", "garionx" };
                        if (validIds.Contains(classification))
                        {
                            return classification!;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auto Routing LLM Error]: {ex.Message}");
            }
        }

        return defaultPersonality;
    }
}
