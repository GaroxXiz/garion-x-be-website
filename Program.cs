using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using GarionX.Repositories;
using GarionX.Usecases;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// 1. Add DbContext with PostgreSQL
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrEmpty(connectionString) && connectionString.StartsWith("postgresql://"))
{
    try
    {
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        // Decode URL encoded values if any (especially password/username)
        username = Uri.UnescapeDataString(username);
        password = Uri.UnescapeDataString(password);

        connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[GarionX Backend] Error parsing PostgreSQL URI: {ex.Message}");
    }
}

if (string.IsNullOrEmpty(connectionString))
{
    var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
    var dbPort = Environment.GetEnvironmentVariable("DB_PORT");
    var dbUser = Environment.GetEnvironmentVariable("DB_USER");
    var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
    var dbName = Environment.GetEnvironmentVariable("DB_NAME");

    if (!string.IsNullOrEmpty(dbHost) && !string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPassword) && !string.IsNullOrEmpty(dbName))
    {
        connectionString = $"Host={dbHost};Port={dbPort ?? "5432"};Database={dbName};Username={dbUser};Password={dbPassword}";
        
        // Auto-append SSL mode for remote PostgreSQL databases (like Supabase)
        if (dbHost != "localhost" && dbHost != "db" && !connectionString.Contains("SSL Mode"))
        {
            connectionString += ";SSL Mode=Require;Trust Server Certificate=true";
        }
    }
    else
    {
        connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=garionx_db;Username=postgres;Password=postgres";
    }
}

builder.Services.AddDbContext<GarionXDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("GarionX")));

// 2. Register Repositories and Services
builder.Services.AddHttpClient();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IAiResponseService, AiResponseService>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

// 3. Add Controllers
builder.Services.AddControllers();

// 4. Configure Authentication using JWT
var jwtSecret = builder.Configuration["JwtSettings:Secret"] ?? "GarionXSuperSecretSecurityKey12345!!!";

// Disable ASP.NET Core's automatic JWT claim type remapping.
// Without this, 'sub' gets silently renamed to ClaimTypes.NameIdentifier
// and User.FindFirst(JwtRegisteredClaimNames.Sub) always returns null.
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "GarionX",
        ValidAudience = builder.Configuration["JwtSettings:Audience"] ?? "GarionX",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };
});

// 5. Configure Swagger with JWT Authorize option
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GarionX API", Version = "v1" });
    
    // Add Security Definition for JWT
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header
            },
            Array.Empty<string>()
        }
    });
});

// 6. Add CORS to support Next.js frontend calls
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 7. Automatically run migrations on startup (resilient approach)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<GarionXDbContext>();
        db.Database.Migrate();
        Console.WriteLine("[GarionX Backend] Database migrations applied successfully.");

        // Synchronize default seeded personalities in DB with current definitions
        var imageGen = db.Personalities.FirstOrDefault(p => p.Id == "image_generator");
        if (imageGen != null)
        {
            imageGen.Name = "Synthetix (Image)";
            imageGen.Description = "A creative cybernetic illustrator. Deconstructs simple prompts into high-fidelity, ultra-rich visual masterpieces.";
            imageGen.SystemPrompt = "You are Synthetix, a cybernetic image generator. Your only function is to generate images based on user prompts. For every request, you MUST: 1. Translate the user prompt to English if necessary. 2. Deconstruct the prompt and expand it into an extremely rich, master-class, high-fidelity visual description (prompt expansion). The expanded prompt must include: a detailed subject description, intricate textures, volumetric lighting, composition details (e.g., octane render, 8k resolution, raytracing, sharp focus, cinematic atmosphere, 35mm lens, HDR), color palette descriptions, and specific style enhancers (e.g., hyperrealistic, cyberpunk neon, surrealist digital art, Unreal Engine 5 render). 3. Generate a markdown image referencing: https://image.pollinations.ai/prompt/{url_encoded_expanded_prompt}?nologo=true. The expanded prompt inside the URL must be fully URL-encoded (spaces replaced with %20, commas with %2C, etc.). 4. Output the markdown image directly. 5. Follow the image with a sleek, structured 'Dossier of Visual Design' breakdown containing: **Core Theme**, **Expanded Prompt details**, **Atmosphere & Lighting**, and **Artistic Influence**. Example format: ![Artwork](https://image.pollinations.ai/prompt/expanded%20description?nologo=true)\n\n### 🎨 DOSSIER OF VISUAL DESIGN\n- **Core Theme**: ...\n- **Expanded Prompt**: ...\n- **Atmosphere**: ...\nNever output code blocks, HTML, or code wrappers around the image markdown.";
            imageGen.AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=image_generator";
        }

        var videoSum = db.Personalities.FirstOrDefault(p => p.Id == "video_summarizer");
        if (videoSum != null)
        {
            videoSum.Name = "VidIntel (Video)";
            videoSum.Description = "An advanced video analysis companion. Upload a video to generate a structured content summary and timeline.";
            videoSum.SystemPrompt = "You are VidIntel, a cybernetic video intelligence analyzer. Your primary function is to summarize and analyze uploaded video files. When a video is uploaded, you must output a structured analysis dossier containing: 1. Video Overview (based on filename and metadata context). 2. Visual & Audio Timeline (a highly detailed breakdown of key events). 3. Key Insights & Summary. 4. Actionable Takeaways. Always maintain a professional, analytical, and highly tech-centric dossier style. If no video has been uploaded yet, politely prompt the user to upload a video for analysis.";
            videoSum.AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=video_summarizer";
        }

        db.SaveChanges();
        Console.WriteLine("[GarionX Backend] Seeded personalities synchronized successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[GarionX Backend] WARNING: Could not apply database migrations or synchronize personalities on startup: {ex.Message}");
        Console.WriteLine("Ensure PostgreSQL is running and your connection string in appsettings.json is correct.");
    }
}

// 8. Configure HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "GarionX API v1"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseCors("AllowFrontend");

app.UseAuthentication(); // Must be called before UseAuthorization
app.UseAuthorization();

app.MapControllers();

app.Run();
