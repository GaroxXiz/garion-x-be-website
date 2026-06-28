using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GarionX.Entities;
using GarionX.Repositories;

namespace GarionX.Usecases;

public interface IAiResponseService
{
    Task<string> GetResponseAsync(Guid chatId, string userMessage, string personalityId, string model);
}

public class AiResponseService : IAiResponseService
{
    private readonly IChatRepository _chatRepository;
    private readonly HttpClient _httpClient;

    public AiResponseService(IChatRepository chatRepository, HttpClient httpClient)
    {
        _chatRepository = chatRepository;
        _httpClient = httpClient;
    }

    public async Task<string> GetResponseAsync(Guid chatId, string userMessage, string personalityId, string model)
    {
        try
        {
            var personality = await _chatRepository.GetPersonalityByIdAsync(personalityId);
            string systemPrompt = personality?.SystemPrompt ?? "You are a helpful assistant.";
            
            // Global instruction for multilingual / auto-translate support
            systemPrompt += "\n\n[System Instruction: You must respond in the same language as the user's message. If the user writes in Indonesian, respond in Indonesian. If they write in English, Spanish, Japanese, French, or any other language, automatically adapt and respond in that exact language while preserving your assigned personality, tone, and character.]";

            // If it's video generator and FAL_API_KEY is missing, instruct the AI to show a notice on how to activate real generation
            if (personalityId == "video_generator" && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FAL_API_KEY")))
            {
                systemPrompt += "\n\n[System Notice: The generative AI video engine is currently running in 'Frontend Cinematic Parallax Simulation' mode because the FAL_API_KEY environment variable is not configured. Append a brief, helpful and stylish siber-note at the end of your response to tell the user that they can configure FAL_API_KEY in Hugging Face Space settings or .env to unlock true generative AI MP4 video files.]";
            }

            var rawHistory = await _chatRepository.GetMessagesAsync(chatId);
            var historyList = rawHistory.ToList();

            // Check if user has uploaded an image (specifically in their latest message)
            var lastUserMsg = historyList.LastOrDefault(m => m.Sender == "user");
            bool hasImage = lastUserMsg != null && lastUserMsg.AttachmentType == "image" && !string.IsNullOrEmpty(lastUserMsg.AttachmentUrl);

            if (personalityId == "video_generator" && !hasImage)
            {
                return "⚠️ **[AnimateX - Warning]** Anda wajib mengunggah gambar (.png, .jpg, .webp) terlebih dahulu sebelum saya dapat menganimasikannya menjadi video. Silakan klik tombol attachment (paperclip) di bawah atau seret gambar Anda ke kolom input chat untuk mengunggah gambar Anda terlebih dahulu!";
            }

            int lastUserIdx = historyList.FindLastIndex(m => m.Sender == "user");
            
            var history = historyList.Select((msg, idx) =>
            {
                string content = idx == lastUserIdx ? userMessage : msg.Content;
                if (!string.IsNullOrEmpty(msg.AttachmentUrl))
                {
                    string attachmentLabel = msg.AttachmentType == "video" ? "Attached Video" : "Attached Image";
                    string fileName = Path.GetFileName(msg.AttachmentUrl);
                    content += $"\n\n[{attachmentLabel}: {fileName}]";
                }
                return new Message
                {
                    Id = msg.Id,
                    ChatId = msg.ChatId,
                    Sender = msg.Sender,
                    Content = content,
                    CreatedAt = msg.CreatedAt,
                    AttachmentUrl = msg.AttachmentUrl,
                    AttachmentType = msg.AttachmentType
                };
            }).ToList();

            var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            bool hasGroqKey = !string.IsNullOrWhiteSpace(groqApiKey) && groqApiKey != "your_groq_api_key_here";

            if (hasGroqKey)
            {

                string groqModel = model.ToLower() switch
                {
                    "gemini" => "gemma2-9b-it",
                    "claude" => "mixtral-8x7b-32768",
                    _ => "llama-3.3-70b-versatile"
                };

                if (hasImage)
                {
                    // Force a vision model on Groq when an image is present
                    groqModel = "meta-llama/llama-4-scout-17b-16e-instruct";
                }

                return model.ToLower() switch
                {
                    "gemini" => await CallGroqAsync(systemPrompt, history, "gemini", groqModel),
                    "claude" => await CallGroqAsync(systemPrompt, history, "claude", groqModel),
                    _ => await CallGroqAsync(systemPrompt, history, "openai", groqModel)
                };
            }
            else
            {
                return model.ToLower() switch
                {
                    "gemini" => await CallGeminiAsync(systemPrompt, history),
                    "claude" => await CallClaudeAsync(systemPrompt, history),
                    _ => await CallOpenAiAsync(systemPrompt, history)
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiResponseService Error] Exception: {ex}");
            return $"❌ Internal error: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Groq — Free, OpenAI-compatible API (replaces paid OpenAI & Claude)
    // Models: llama-3.3-70b-versatile, compound-beta, gemma2-9b-it, etc.
    // Docs: https://console.groq.com/docs/openai
    // ─────────────────────────────────────────────────────────────────────
    private async Task<string> CallGroqAsync(
        string systemPrompt,
        IEnumerable<Message> history,
        string slot,        // "openai" | "gemini" | "claude" — for token tracking label
        string groqModel)
    {
        var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

        // Graceful fallback: if no Groq key, try original provider
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_groq_api_key_here")
        {
            return slot.ToLower() switch
            {
                "openai" => await CallOpenAiAsync(systemPrompt, history),
                "gemini" => await CallGeminiAsync(systemPrompt, history),
                _ => await CallClaudeAsync(systemPrompt, history)
            };
        }

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        foreach (var msg in history.TakeLast(11))
        {
            if (msg.Sender == "user" && msg.AttachmentType == "image" && !string.IsNullOrEmpty(msg.AttachmentUrl))
            {
                var imageInfo = TryGetImageBase64(msg.AttachmentUrl);
                if (imageInfo != null)
                {
                    messages.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = msg.Content },
                            new { type = "image_url", image_url = new { url = $"data:{imageInfo.Value.mimeType};base64,{imageInfo.Value.base64Data}" } }
                        }
                    });
                    continue;
                }
            }
            messages.Add(new { role = msg.Sender == "user" ? "user" : "assistant", content = msg.Content });
        }

        var payload = new { model = groqModel, messages, temperature = 0.7 };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Groq Error ({slot})] HTTP {(int)response.StatusCode}: {rawBody}");
            string friendly = TryExtractErrorMessage(rawBody);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    $"❌ Groq: API key tidak valid. Periksa `GROQ_API_KEY` di file `.env`.\n\nDetail: {friendly}",
                System.Net.HttpStatusCode.TooManyRequests =>
                    $"❌ Groq: Rate limit tercapai. Coba lagi sebentar.\n\nDetail: {friendly}",
                System.Net.HttpStatusCode.BadRequest =>
                    $"❌ Groq: Model '{groqModel}' tidak ditemukan atau request tidak valid.\n\nDetail: {friendly}",
                _ => $"❌ Groq error ({(int)response.StatusCode}): {friendly}"
            };
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        // Track tokens
        if (root.TryGetProperty("usage", out var usage))
        {
            long totalTokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt64() : 0;
            if (totalTokens > 0)
                await _chatRepository.IncrementTokenUsageAsync(slot, totalTokens);
        }

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                return content.GetString() ?? "";
        }

        return "[Failed to parse Groq response]";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Gemini — Free via Google AI Studio (15 req/min, 1500 req/day)
    // Get key: https://aistudio.google.com/apikey
    // ─────────────────────────────────────────────────────────────────────
    private async Task<string> CallGeminiAsync(string systemPrompt, IEnumerable<Message> history)
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "⚠️ Gemini API Key belum dikonfigurasi. Tambahkan `GEMINI_API_KEY` di file `.env`.\nDapatkan key gratis di: https://aistudio.google.com/apikey";

        var contentsList = new List<GeminiContent>();
        bool foundUser = false;
        foreach (var msg in history.TakeLast(11))
        {
            if (!foundUser && msg.Sender != "user") continue;
            foundUser = true;

            string role = msg.Sender == "user" ? "user" : "model";
            var msgParts = new List<object> { new { text = msg.Content } };

            if (msg.Sender == "user" && msg.AttachmentType == "image" && !string.IsNullOrEmpty(msg.AttachmentUrl))
            {
                var imageInfo = TryGetImageBase64(msg.AttachmentUrl);
                if (imageInfo != null)
                {
                    msgParts.Add(new
                    {
                        inlineData = new
                        {
                            mimeType = imageInfo.Value.mimeType,
                            data = imageInfo.Value.base64Data
                        }
                    });
                }
            }

            if (contentsList.Count > 0 && contentsList[^1].role == role)
            {
                contentsList[^1].parts.AddRange(msgParts);
            }
            else
            {
                contentsList.Add(new GeminiContent { role = role, parts = msgParts });
            }
        }

        if (contentsList.Count == 0)
        {
            contentsList.Add(new GeminiContent
            {
                role = "user",
                parts = new List<object> { new { text = "Hello" } }
            });
        }

        var payload = new
        {
            contents = contentsList,
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            generationConfig = new { temperature = 0.7, maxOutputTokens = 2048 }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Gemini Error] HTTP {(int)response.StatusCode}: {rawBody}");
            string friendly = TryExtractErrorMessage(rawBody);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                    $"❌ Gemini: API key tidak valid. Periksa `GEMINI_API_KEY` di file `.env`.\nDapatkan key gratis di: https://aistudio.google.com/apikey\n\nDetail: {friendly}",
                System.Net.HttpStatusCode.TooManyRequests =>
                    $"❌ Gemini: Rate limit tercapai (batas gratis: 15 req/menit, 1500 req/hari). Coba lagi sebentar.\n\nDetail: {friendly}",
                System.Net.HttpStatusCode.NotFound =>
                    $"❌ Gemini: Model tidak ditemukan. API key mungkin tidak punya akses ke Gemini 2.0 Flash.\n\nDetail: {friendly}",
                _ => $"❌ Gemini error ({(int)response.StatusCode}): {friendly}"
            };
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("usageMetadata", out var usageMeta))
        {
            long total = usageMeta.TryGetProperty("totalTokenCount", out var ttc) ? ttc.GetInt64() : 0;
            if (total > 0) await _chatRepository.IncrementTokenUsageAsync("gemini", total);
        }

        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                return parts[0].TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

            if (candidate.TryGetProperty("finishReason", out var reason))
                return reason.GetString() switch
                {
                    "SAFETY" => "⚠️ Gemini: Respons diblokir oleh filter keamanan. Coba ubah pertanyaanmu.",
                    "MAX_TOKENS" => "⚠️ Gemini: Respons terpotong karena terlalu panjang.",
                    _ => "[Gemini mengembalikan respons kosong]"
                };
        }

        return "[Gagal membaca respons Gemini]";
    }

    // ─────────────────────────────────────────────────────────────────────
    // OpenAI — Berbayar, hanya dipakai jika GROQ_API_KEY tidak ada
    // https://platform.openai.com/settings/billing
    // ─────────────────────────────────────────────────────────────────────
    private async Task<string> CallOpenAiAsync(string systemPrompt, IEnumerable<Message> history)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "⚠️ OpenAI API Key belum dikonfigurasi. Tambahkan `OPENAI_API_KEY` di `.env`, atau isi `GROQ_API_KEY` untuk alternatif gratis.";

        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var msg in history.TakeLast(11))
        {
            if (msg.Sender == "user" && msg.AttachmentType == "image" && !string.IsNullOrEmpty(msg.AttachmentUrl))
            {
                var imageInfo = TryGetImageBase64(msg.AttachmentUrl);
                if (imageInfo != null)
                {
                    messages.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = msg.Content },
                            new { type = "image_url", image_url = new { url = $"data:{imageInfo.Value.mimeType};base64,{imageInfo.Value.base64Data}" } }
                        }
                    });
                    continue;
                }
            }
            messages.Add(new { role = msg.Sender == "user" ? "user" : "assistant", content = msg.Content });
        }

        var payload = new { model = "gpt-4o-mini", messages, temperature = 0.7 };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            string friendly = TryExtractErrorMessage(rawBody);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    "❌ OpenAI: API key tidak valid.",
                System.Net.HttpStatusCode.TooManyRequests =>
                    $"❌ OpenAI: Kuota habis atau rate limit. Isi kredit di https://platform.openai.com/settings/billing, atau gunakan Groq (gratis) dengan mengisi `GROQ_API_KEY` di `.env`.\n\nDetail: {friendly}",
                _ => $"❌ OpenAI error ({(int)response.StatusCode}): {friendly}"
            };
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        if (root.TryGetProperty("usage", out var usage))
        {
            long total = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt64() : 0;
            if (total > 0) await _chatRepository.IncrementTokenUsageAsync("openai", total);
        }
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                return content.GetString() ?? "";
        }
        return "[Gagal membaca respons OpenAI]";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Claude — Berbayar, hanya dipakai jika GROQ_API_KEY tidak ada
    // https://console.anthropic.com
    // ─────────────────────────────────────────────────────────────────────
    private async Task<string> CallClaudeAsync(string systemPrompt, IEnumerable<Message> history)
    {
        var apiKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "⚠️ Claude API Key belum dikonfigurasi. Isi `CLAUDE_API_KEY` di `.env`, atau gunakan Groq (gratis) dengan mengisi `GROQ_API_KEY`.";

        var messagesList = new List<ClaudeMessage>();
        foreach (var msg in history.TakeLast(11))
        {
            var role = msg.Sender == "user" ? "user" : "assistant";

            var blocks = new List<object> { new { type = "text", text = msg.Content } };
            if (msg.Sender == "user" && msg.AttachmentType == "image" && !string.IsNullOrEmpty(msg.AttachmentUrl))
            {
                var imageInfo = TryGetImageBase64(msg.AttachmentUrl);
                if (imageInfo != null)
                {
                    blocks.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = imageInfo.Value.mimeType,
                            data = imageInfo.Value.base64Data
                        }
                    });
                }
            }

            if (messagesList.Count > 0 && messagesList[^1].role == role)
            {
                messagesList[^1].content.AddRange(blocks);
            }
            else
            {
                messagesList.Add(new ClaudeMessage { role = role, content = blocks });
            }
        }

        while (messagesList.Count > 0 && messagesList[0].role != "user")
        {
            messagesList.RemoveAt(0);
        }

        if (messagesList.Count == 0) return "[Tidak ada pesan user untuk Claude]";

        var payload = new { model = "claude-3-5-haiku-20241022", max_tokens = 2048, system = systemPrompt, messages = messagesList };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            string friendly = TryExtractErrorMessage(rawBody);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests =>
                    $"❌ Claude: Kuota habis. Isi kredit di https://console.anthropic.com, atau gunakan Groq (gratis) dengan mengisi `GROQ_API_KEY` di `.env`.\n\nDetail: {friendly}",
                _ => $"❌ Claude error ({(int)response.StatusCode}): {friendly}"
            };
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        if (root.TryGetProperty("usage", out var usage))
        {
            long total = (usage.TryGetProperty("input_tokens", out var it) ? it.GetInt64() : 0)
                       + (usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt64() : 0);
            if (total > 0) await _chatRepository.IncrementTokenUsageAsync("claude", total);
        }
        if (root.TryGetProperty("content", out var contentArr) && contentArr.GetArrayLength() > 0)
        {
            if (contentArr[0].TryGetProperty("text", out var text))
                return text.GetString() ?? "";
        }
        return "[Gagal membaca respons Claude]";
    }

    private static string TryExtractErrorMessage(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return "Tidak ada detail error.";
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m))
                    return m.GetString() ?? rawBody;
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString() ?? rawBody;
            }
            if (root.TryGetProperty("message", out var topMsg))
                return topMsg.GetString() ?? rawBody;
        }
        catch { }
        return rawBody.Length > 300 ? rawBody[..300] + "..." : rawBody;
    }

    private static (string mimeType, string base64Data)? TryGetImageBase64(string relativeUrl)
    {
        if (string.IsNullOrEmpty(relativeUrl)) return null;
        try
        {
            var fileName = relativeUrl.TrimStart('/');
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fileName);
            if (!File.Exists(filePath)) return null;

            var bytes = File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(bytes);

            var extension = Path.GetExtension(filePath).ToLower();
            var mimeType = extension switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            return (mimeType, base64);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiResponseService] Failed to read image bytes: {ex.Message}");
            return null;
        }
    }

    private class GeminiContent
    {
        public string role { get; set; } = "";
        public List<object> parts { get; set; } = new();
    }

    private class ClaudeMessage
    {
        public string role { get; set; } = "";
        public List<object> content { get; set; } = new();
    }
}
