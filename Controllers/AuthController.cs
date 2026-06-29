using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using GarionX.Entities;
using GarionX.Repositories;
using GarionX.Dtos;
using GarionX.Usecases;

namespace GarionX.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IChatRepository _chatRepository;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly HttpClient _httpClient;

    public AuthController(IChatRepository chatRepository, IJwtTokenGenerator tokenGenerator, HttpClient httpClient)
    {
        _chatRepository = chatRepository;
        _tokenGenerator = tokenGenerator;
        _httpClient = httpClient;
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username and Password are required.");
        }

        var existingUser = await _chatRepository.GetUserByUsernameAsync(request.Username);
        if (existingUser != null)
        {
            return Conflict("Username is already taken.");
        }

        var newUser = new User
        {
            Username = request.Username,
            PasswordHash = HashPassword(request.Password),
            Email = string.IsNullOrWhiteSpace(request.Email) ? $"{request.Username}@garionx.ai" : request.Email,
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.Username : request.Name,
            AvatarUrl = $"https://api.dicebear.com/7.x/bottts/svg?seed={Uri.EscapeDataString(request.Username)}"
        };

        var registeredUser = await _chatRepository.RegisterUserAsync(newUser);
        var token = _tokenGenerator.GenerateToken(registeredUser);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = registeredUser.Username,
            Email = registeredUser.Email,
            Name = registeredUser.Name,
            AvatarUrl = registeredUser.AvatarUrl
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Email and Password are required.");
        }

        var user = await _chatRepository.GetUserByUsernameAsync(request.Email);
        if (user == null)
        {
            return Unauthorized("Invalid email or password.");
        }

        var inputHash = HashPassword(request.Password);
        if (user.PasswordHash != inputHash)
        {
            return Unauthorized("Invalid email or password.");
        }

        var token = _tokenGenerator.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = user.Username,
            Email = user.Email,
            Name = user.Name,
            AvatarUrl = user.AvatarUrl
        });
    }

    private async Task<List<SecurityKey>> GetFirebaseSigningKeysAsync()
    {
        using var response = await _httpClient.GetAsync("https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com");
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Failed to fetch Firebase public certificates.");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        var keys = new List<SecurityKey>();
        foreach (var prop in root.EnumerateObject())
        {
            var certString = prop.Value.GetString();
            if (string.IsNullOrEmpty(certString)) continue;

            var cert = X509CertificateLoader.LoadCertificate(Encoding.UTF8.GetBytes(certString));
            keys.Add(new X509SecurityKey(cert));
        }

        return keys;
    }

    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return BadRequest("Firebase ID Token is required.");
        }

        // 1. Verify Firebase ID Token cryptographically
        var projectId = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") ?? "garionx-c6368";
        
        string email;
        string name;
        string? picture = null;

        try
        {
            var keys = await GetFirebaseSigningKeysAsync();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://securetoken.google.com/{projectId}",
                ValidateAudience = true,
                ValidAudience = projectId,
                ValidateLifetime = true,
                IssuerSigningKeys = keys,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            tokenHandler.InboundClaimTypeMap.Clear();
            var principal = tokenHandler.ValidateToken(request.IdToken, validationParameters, out var validatedToken);

            var emailClaim = principal.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(emailClaim))
            {
                return BadRequest("Failed to retrieve email claim from Firebase ID Token.");
            }

            email = emailClaim;
            name = principal.FindFirst("name")?.Value ?? request.DisplayName ?? email;
            picture = principal.FindFirst("picture")?.Value ?? request.PhotoUrl;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Firebase Token Verification Error] Validation failed: {ex.Message}");
            return BadRequest($"Failed to authorize with Google: Invalid Firebase ID Token. Error: {ex.Message}");
        }

        // 2. Find or register the user in our DB (username is matched to Google email)
        var user = await _chatRepository.GetUserByUsernameAsync(email);
        if (user == null)
        {
            user = new User
            {
                Username = email,
                PasswordHash = "FIREBASE_AUTH_" + Guid.NewGuid().ToString(), // Placeholder password
                Email = email,
                Name = name,
                AvatarUrl = picture ?? $"https://api.dicebear.com/7.x/bottts/svg?seed={Uri.EscapeDataString(email)}"
            };
            user = await _chatRepository.RegisterUserAsync(user);
        }
        else if (picture != null && user.AvatarUrl != picture)
        {
            // Update profile avatar if it changed on Google side
            await _chatRepository.UpdateUserProfileAsync(user.Id, user.Name, user.Email, picture);
            user = await _chatRepository.GetUserByIdAsync(user.Id) ?? user;
        }

        // 3. Generate system token
        var token = _tokenGenerator.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = user.Username,
            Email = user.Email,
            Name = user.Name,
            AvatarUrl = user.AvatarUrl
        });
    }

    [HttpPost("create-user")]
    public async Task<ActionResult<AuthResponse>> CreateUser([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username and Password are required.");
        }

        var existingUser = await _chatRepository.GetUserByUsernameAsync(request.Username);
        if (existingUser != null)
        {
            return Conflict("Username is already taken.");
        }

        var newUser = new User
        {
            Username = request.Username,
            PasswordHash = HashPassword(request.Password),
            Email = string.IsNullOrWhiteSpace(request.Email) ? $"{request.Username}@garionx.ai" : request.Email,
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.Username : request.Name,
            AvatarUrl = $"https://api.dicebear.com/7.x/bottts/svg?seed={Uri.EscapeDataString(request.Username)}"
        };

        var registeredUser = await _chatRepository.RegisterUserAsync(newUser);
        var createdUserToken = _tokenGenerator.GenerateToken(registeredUser);

        return Ok(new AuthResponse
        {
            Token = createdUserToken,
            Username = registeredUser.Username,
            Email = registeredUser.Email,
            Name = registeredUser.Name,
            AvatarUrl = registeredUser.AvatarUrl
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest("Username and NewPassword are required.");
        }

        var user = await _chatRepository.GetUserByUsernameAsync(request.Username);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var newHash = HashPassword(request.NewPassword);
        await _chatRepository.UpdateUserPasswordAsync(request.Username, newHash);

        return Ok(new { message = "Password has been reset successfully." });
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<ActionResult<AuthResponse>> GetProfile()
    {
        var userIdString = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value 
                           ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized("Invalid token claims.");
        }

        var user = await _chatRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        return Ok(new AuthResponse
        {
            Token = _tokenGenerator.GenerateToken(user),
            Username = user.Username,
            Email = user.Email,
            Name = user.Name,
            AvatarUrl = user.AvatarUrl
        });
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult<AuthResponse>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userIdString = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value 
                           ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized("Invalid token claims.");
        }

        var user = await _chatRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Name and Email are required.");
        }

        var userByEmail = await _chatRepository.GetUserByUsernameAsync(request.Email);
        if (userByEmail != null && userByEmail.Id != userId)
        {
            return Conflict("Email is already taken.");
        }

        await _chatRepository.UpdateUserProfileAsync(userId, request.Name, request.Email, null);
        
        var updatedUser = await _chatRepository.GetUserByIdAsync(userId);
        var token = _tokenGenerator.GenerateToken(updatedUser!);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = updatedUser!.Username,
            Email = updatedUser.Email,
            Name = updatedUser.Name,
            AvatarUrl = updatedUser.AvatarUrl
        });
    }

    [Authorize]
    [HttpPost("profile/upload-avatar")]
    public async Task<ActionResult<AuthResponse>> UploadAvatar(IFormFile file)
    {
        var userIdString = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value 
                           ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized("Invalid token claims.");
        }

        var user = await _chatRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var extension = Path.GetExtension(file.FileName).ToLower();
        if (!allowedExtensions.Contains(extension))
        {
            return BadRequest("Invalid image format. Supported formats: JPG, JPEG, PNG, WEBP, GIF.");
        }

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var uniqueFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var avatarUrl = $"/uploads/{uniqueFileName}";
        await _chatRepository.UpdateUserProfileAsync(userId, user.Name, user.Email, avatarUrl);

        var updatedUser = await _chatRepository.GetUserByIdAsync(userId);
        var token = _tokenGenerator.GenerateToken(updatedUser!);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = updatedUser!.Username,
            Email = updatedUser.Email,
            Name = updatedUser.Name,
            AvatarUrl = updatedUser.AvatarUrl
        });
    }

    private static readonly Dictionary<string, (string Otp, DateTime Expiry)> OtpStore = new();

    private static void SendOtpViaGmail(string toEmail, string otp)
    {
        var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER");
        var smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS");
        var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
        var smtpPortStr = Environment.GetEnvironmentVariable("SMTP_PORT");
        int smtpPort = int.TryParse(smtpPortStr, out var port) ? port : 587;

        if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
        {
            Console.WriteLine($"[GARIONX SMTP WARNING] SMTP_USER or SMTP_PASS not set. Falling back to console only. OTP for {toEmail} is: {otp}");
            return;
        }

        try
        {
            using var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new System.Net.NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true,
                Timeout = 5000
            };

            var mailMessage = new System.Net.Mail.MailMessage
            {
                From = new System.Net.Mail.MailAddress(smtpUser, "Garion-X Terminal Services"),
                Subject = "🔑 Garion-X Terminal OTP Security Code",
                Body = $@"
<div style=""font-family: monospace; background-color: #0d0e15; color: #e2e8f0; padding: 30px; border: 1px solid #1e293b; border-radius: 8px; max-width: 500px; margin: auto;"">
    <h2 style=""color: #00ffcc; text-align: center; text-transform: uppercase; letter-spacing: 2px;"">Garion-X Terminal Auth</h2>
    <hr style=""border-color: #334155; margin: 20px 0;"" />
    <p>System requested a security code for your session.</p>
    <p>Please enter the following 6-digit OTP code to complete authorization:</p>
    <div style=""background: rgba(0, 255, 204, 0.05); border: 1px dashed #00ffcc; padding: 15px; border-radius: 6px; text-align: center; margin: 25px 0;"">
        <span style=""font-size: 2.2rem; font-weight: bold; color: #00ffcc; letter-spacing: 8px; font-family: monospace;"">{otp}</span>
    </div>
    <p style=""font-size: 0.8rem; color: #64748b; text-align: center;"">This security code is strictly confidential and will expire in <strong>5 minutes</strong>.</p>
    <p style=""font-size: 0.8rem; color: #64748b; text-align: center;"">If you did not request this code, please ignore this email.</p>
</div>",
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);
            client.Send(mailMessage);
            Console.WriteLine($"[GARIONX SMTP] OTP email sent successfully to {toEmail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GARIONX SMTP ERROR] Failed to send email to {toEmail}: {ex.Message}");
            throw; // Rethrow to propagate to the controller
        }
    }

    private async Task<bool> SendOtpViaResendAsync(string toEmail, string otp)
    {
        var resendApiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
        if (string.IsNullOrWhiteSpace(resendApiKey))
        {
            return false;
        }

        try
        {
            var emailData = new
            {
                from = "Garion-X Terminal <onboarding@resend.dev>",
                to = new[] { toEmail },
                subject = "🔑 Garion-X Terminal OTP Security Code",
                html = $@"
<div style=""font-family: monospace; background-color: #0d0e15; color: #e2e8f0; padding: 30px; border: 1px solid #1e293b; border-radius: 8px; max-width: 500px; margin: auto;"">
    <h2 style=""color: #00ffcc; text-align: center; text-transform: uppercase; letter-spacing: 2px;"">Garion-X Terminal Auth</h2>
    <hr style=""border-color: #334155; margin: 20px 0;"" />
    <p>System requested a security code for your session.</p>
    <p>Please enter the following 6-digit OTP code to complete authorization:</p>
    <div style=""background: rgba(0, 255, 204, 0.05); border: 1px dashed #00ffcc; padding: 15px; border-radius: 6px; text-align: center; margin: 25px 0;"">
        <span style=""font-size: 2.2rem; font-weight: bold; color: #00ffcc; letter-spacing: 8px; font-family: monospace;"">{otp}</span>
    </div>
    <p style=""font-size: 0.8rem; color: #64748b; text-align: center;"">This security code is strictly confidential and will expire in <strong>5 minutes</strong>.</p>
    <p style=""font-size: 0.8rem; color: #64748b; text-align: center;"">If you did not request this code, please ignore this email.</p>
</div>"
            };

            var jsonContent = JsonSerializer.Serialize(emailData);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            requestMessage.Headers.Add("Authorization", $"Bearer {resendApiKey}");
            requestMessage.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(requestMessage);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GARIONX RESEND] OTP email sent successfully to {toEmail}");
                return true;
            }
            else
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[GARIONX RESEND ERROR] Resend API failed: {response.StatusCode} - {errorResponse}");
                throw new Exception($"Resend API Error: {response.StatusCode} - {errorResponse}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GARIONX RESEND ERROR] Failed to send email to {toEmail} via Resend: {ex.Message}");
            throw;
        }
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Email is required.");
        }

        var resendApiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
        var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER");
        var smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS");

        var isMock = string.IsNullOrWhiteSpace(resendApiKey) && 
                     (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass));

        // Generate 6 digit OTP
        var random = new Random();
        var otp = random.Next(100000, 999999).ToString();
        var expiry = DateTime.UtcNow.AddMinutes(5);

        // Store OTP
        OtpStore[request.Email.ToLower()] = (otp, expiry);

        if (isMock)
        {
            Console.WriteLine($"[GARIONX OTP] (SIMULATED DEV) Code for {request.Email} is: {otp}");
            return Ok(new { 
                message = "OTP simulated in dev console.", 
                otp = otp, 
                isMock = true 
            });
        }

        // Try Resend API if API Key is configured
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            try
            {
                await SendOtpViaResendAsync(request.Email, otp);
                return Ok(new { 
                    message = "OTP sent successfully via Resend HTTP API.", 
                    otp = (string?)null, 
                    isMock = false 
                });
            }
            catch (Exception ex)
            {
                return BadRequest($"Gagal mengirim email OTP via Resend API. Detail Error: {ex.Message}");
            }
        }

        try
        {
            // Send OTP via Gmail SMTP
            SendOtpViaGmail(request.Email, otp);
            
            return Ok(new { 
                message = "OTP sent successfully via Gmail.", 
                otp = (string?)null, 
                isMock = false 
            });
        }
        catch (Exception ex)
        {
            return BadRequest($"Gagal mengirim email OTP ke Gmail Anda. Detail Error: {ex.Message}");
        }
    }

    [HttpPost("verify-otp")]
    public async Task<ActionResult<AuthResponse>> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Otp))
        {
            return BadRequest("Email and OTP are required.");
        }

        var emailKey = request.Email.ToLower();
        if (!OtpStore.TryGetValue(emailKey, out var value) || value.Otp != request.Otp || DateTime.UtcNow > value.Expiry)
        {
            return BadRequest("Invalid or expired OTP code.");
        }

        // OTP is verified, remove it
        OtpStore.Remove(emailKey);

        // Find user by email
        var user = await _chatRepository.GetUserByUsernameAsync(request.Email);
        if (user == null)
        {
            // If user doesn't exist, register them on the fly
            var username = request.Email.Split('@')[0];
            // Ensure unique username
            var suffix = 0;
            var uniqueUsername = username;
            while (await _chatRepository.GetUserByUsernameAsync(uniqueUsername) != null)
            {
                suffix++;
                uniqueUsername = $"{username}{suffix}";
            }

            user = new User
            {
                Username = uniqueUsername,
                PasswordHash = HashPassword(Guid.NewGuid().ToString()), // Random hash for passwordless
                Email = request.Email,
                Name = string.IsNullOrWhiteSpace(request.Name) ? username : request.Name,
                AvatarUrl = $"https://api.dicebear.com/7.x/bottts/svg?seed={Uri.EscapeDataString(uniqueUsername)}"
            };
            user = await _chatRepository.RegisterUserAsync(user);
        }

        var token = _tokenGenerator.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = user.Username,
            Email = user.Email,
            Name = user.Name,
            AvatarUrl = user.AvatarUrl
        });
    }
}
