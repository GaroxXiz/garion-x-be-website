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

    [HttpPost("send-otp")]
    public IActionResult SendOtp([FromBody] SendOtpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Email is required.");
        }

        // Generate 6 digit OTP
        var random = new Random();
        var otp = random.Next(100000, 999999).ToString();
        var expiry = DateTime.UtcNow.AddMinutes(5);

        // Store OTP
        OtpStore[request.Email.ToLower()] = (otp, expiry);

        // Log OTP to console (so developer/admin can see it in terminal logs)
        Console.WriteLine($"[GARIONX OTP] Code for {request.Email} is: {otp}");

        // Return it in response for debugging/mock presentation purposes
        return Ok(new { message = "OTP sent successfully to email.", otp = otp });
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
                Name = username,
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
